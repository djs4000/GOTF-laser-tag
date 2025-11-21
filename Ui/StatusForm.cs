using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using LaserTag.Defusal.Domain;
using LaserTag.Defusal.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LaserTag.Defusal.Ui;

/// <summary>
/// Compact always-on-top status surface that surfaces the current match snapshot.
/// </summary>
public sealed class StatusForm : Form
{
    private readonly MatchCoordinator _coordinator;
    private readonly ILogger<StatusForm> _logger;
    private readonly IFocusService _focusService;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly System.Windows.Forms.Timer _focusTimer;
    private MatchStateSnapshot _snapshot = MatchStateSnapshot.Default;
    private FocusWindowInfo _focusWindowInfo = FocusWindowInfo.Empty;

    private readonly Label _matchLabel = new();
    private readonly Label _httpLabel = new();
    private readonly Label _stateLabel = new();
    private readonly Label _overtimeLabel = new();
    private readonly Label _propLabel = new();
    private readonly Label _plantLabel = new();
    private readonly Label _latencyLabel = new();
    private readonly Label _focusLabel = new();
    private readonly Label _actionLabel = new();
    private readonly Button _focusButton = new();

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool AllowClose { get; set; }

    public StatusForm(
        MatchCoordinator coordinator,
        IFocusService focusService,
        IOptions<MatchOptions> options,
        HttpEndpointMetadata endpointMetadata,
        ILogger<StatusForm> logger)
    {
        _coordinator = coordinator;
        _logger = logger;
        _focusService = focusService;

        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = false;
        Text = "ICE Defusal Monitor";
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 8,
            Padding = new Padding(10),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        ConfigureHttpLabel(endpointMetadata);
        AddRow(layout, "HTTP", _httpLabel);
        AddRow(layout, "Match", _matchLabel);
        AddRow(layout, "State", CreateStatePanel());
        AddRow(layout, "Prop", _propLabel);
        AddRow(layout, "Plant", _plantLabel);
        AddRow(layout, "Clock", _latencyLabel);
        AddRow(layout, "Focus", CreateFocusPanel());
        AddRow(layout, "Last", _actionLabel);

        Controls.Add(layout);

        var refreshInterval = Math.Max(100, 1000 / Math.Max(1, options.Value.ClockExpectedHz));
        _refreshTimer = new System.Windows.Forms.Timer { Interval = refreshInterval };
        _refreshTimer.Tick += (_, _) => RenderSnapshot();
        _refreshTimer.Start();

        _focusTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _focusTimer.Tick += OnFocusTimerTick;
        _focusTimer.Start();

        _coordinator.SnapshotUpdated += OnSnapshotUpdated;
        RenderSnapshot();
    }

    private Control CreateStatePanel()
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight
        };

        _stateLabel.AutoSize = true;
        _stateLabel.Font = new Font(Font, FontStyle.Bold);

        _overtimeLabel.AutoSize = true;
        _overtimeLabel.Visible = false;
        _overtimeLabel.Margin = new Padding(8, 0, 0, 0);
        _overtimeLabel.Padding = new Padding(6, 2, 6, 2);
        _overtimeLabel.BackColor = Color.DarkOrange;
        _overtimeLabel.ForeColor = Color.White;
        _overtimeLabel.Text = "OVERTIME";

        panel.Controls.Add(_stateLabel);
        panel.Controls.Add(_overtimeLabel);
        return panel;
    }

    private Control CreateFocusPanel()
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight
        };

        _focusLabel.AutoSize = true;

        _focusButton.AutoSize = true;
        _focusButton.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _focusButton.Margin = new Padding(8, 0, 0, 0);
        _focusButton.Text = "Focus window";
        _focusButton.Click += OnFocusButtonClick;

        panel.Controls.Add(_focusLabel);
        panel.Controls.Add(_focusButton);
        return panel;
    }

    private void AddRow(TableLayoutPanel layout, string label, Control control)
    {
        var header = new Label
        {
            Text = label + ":",
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(0, 3, 8, 3)
        };

        control.AutoSize = true;
        control.Margin = new Padding(0, 3, 0, 3);
        layout.Controls.Add(header);
        layout.Controls.Add(control);
    }

    private void OnSnapshotUpdated(object? sender, MatchStateSnapshot snapshot)
    {
        _snapshot = snapshot;
        if (IsHandleCreated)
        {
            BeginInvoke(RenderSnapshot);
        }
    }

    private void RenderSnapshot()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        var snapshot = _snapshot;
        _matchLabel.Text = snapshot.MatchId ?? "—";
        _stateLabel.Text = snapshot.LifecycleState.ToString();
        _propLabel.Text = snapshot.PropReplyStatus ?? snapshot.PropState.ToString();

        _plantLabel.Text = snapshot.PlantTimeSec.HasValue
            ? $"{snapshot.PlantTimeSec.Value:F1}s"
            : "—";

        _latencyLabel.Text = snapshot.LastClockLatency.HasValue
            ? $"Latency {snapshot.LastClockLatency.Value.TotalMilliseconds:F0} ms"
            : "Latency —";

        UpdateFocusLabel();

        _actionLabel.Text = snapshot.LastActionDescription;

        if (snapshot.IsOvertime && snapshot.OvertimeRemainingSec is not null)
        {
            _overtimeLabel.Visible = true;
            _overtimeLabel.Text = $"OVERTIME {Math.Max(0, snapshot.OvertimeRemainingSec.Value):F1}s";
        }
        else
        {
            _overtimeLabel.Visible = false;
            _overtimeLabel.Text = "OVERTIME";
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            var context = SynchronizationContext.Current;
            if (context is not null)
            {
                _focusService.BindToUiThread(context);
            }
            _logger.LogInformation("Status window handle created");
        }
        catch
        {
            // Ignore logging failures during shutdown.
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _coordinator.SnapshotUpdated -= OnSnapshotUpdated;
            _refreshTimer.Dispose();
            _focusTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!AllowClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }
    
    private void OnFocusTimerTick(object? sender, EventArgs e)
    {
        try
        {
            _focusWindowInfo = _focusService.GetForegroundWindowInfo();
            RenderSnapshot();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to poll foreground window info");
        }
    }

    private void ConfigureHttpLabel(HttpEndpointMetadata metadata)
    {
        if (metadata.Urls.Count == 0)
        {
            _httpLabel.Text = "—";
            return;
        }

        var formatted = metadata.Urls
            .Select(FormatEndpointDisplay)
            .ToArray();
        _httpLabel.Text = string.Join(Environment.NewLine, formatted);
    }

    private static string FormatEndpointDisplay(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? $"{uri.Host}:{uri.Port}"
            : url;
    }

    private void UpdateFocusLabel()
    {
        var info = _focusWindowInfo;
        if (info.IsTargetForeground)
        {
            var title = string.IsNullOrWhiteSpace(info.WindowTitle)
                ? $"0x{info.Handle.ToInt64():X}"
                : info.WindowTitle;
            _focusLabel.Text = $"Target in focus ({title})";
            _focusLabel.ForeColor = Color.DarkGreen;
            return;
        }

        if (info.Handle != IntPtr.Zero)
        {
            var title = string.IsNullOrWhiteSpace(info.WindowTitle)
                ? $"0x{info.Handle.ToInt64():X}"
                : info.WindowTitle;
            _focusLabel.Text = $"Foreground: {title}";
            _focusLabel.ForeColor = Color.DarkRed;
            return;
        }

        _focusLabel.Text = "Awaiting focus";
        _focusLabel.ForeColor = Color.DarkRed;
    }

    private async void OnFocusButtonClick(object? sender, EventArgs e)
    {
        try
        {
            _focusButton.Enabled = false;
            var result = await _focusService.TryFocusWindowAsync("Manual focus", CancellationToken.None).ConfigureAwait(true);
            var description = result.FocusAcquired
                ? $"Manual focus succeeded: {result.Description}"
                : $"Manual focus failed: {result.Description}";
            _coordinator.ReportFocusResult(result.FocusAcquired, description);
            if (!result.FocusAcquired)
            {
                _logger.LogWarning("Manual focus failed: {Description}", result.Description);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual focus attempt threw an exception");
            _coordinator.ReportFocusResult(false, "Manual focus encountered an error");
        }
        finally
        {
            _focusButton.Enabled = true;
        }
    }
}
