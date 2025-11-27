using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    private readonly MatchOptions _matchOptions;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly System.Windows.Forms.Timer _focusTimer;
    private readonly System.Windows.Forms.Timer _debugGameTimer;
    private MatchStateSnapshot _snapshot = MatchStateSnapshot.Default;
    private FocusWindowInfo _focusWindowInfo = FocusWindowInfo.Empty;

    private readonly Label _matchLabel = new();
    private readonly Label _httpLabel = new();
    private readonly Label _stateLabel = new();
    private readonly Label _overtimeLabel = new();
    private readonly Label _propLabel = new();
    private readonly Label _plantLabel = new();
    private readonly Label _latencyLabel = new();
    private readonly Label _defuseTimerLabel = new();
    private readonly Label _matchTimerLabel = new();
    private readonly Label _focusLabel = new();
    private readonly Label _actionLabel = new();
    private readonly Button _focusButton = new();
    private const double CountdownDebugDurationSec = 5;
    private const double RunningDebugDurationSec = 219;
    private double _debugElapsedSec;
    private double _debugTimerDurationSec;
    private MatchSnapshotStatus? _debugTimerStatus;
    private string? _debugMatchId;

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
        _matchOptions = options.Value;

        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = false;
        Text = "ICE Defusal Monitor";
        AutoSize = false;
        AutoScroll = true;
        AutoSizeMode = AutoSizeMode.GrowOnly;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 11,
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
        AddRow(layout, "Bomb", _plantLabel);
        AddRow(layout, "Clock", _latencyLabel);
        AddRow(layout, "Defuse timer", _defuseTimerLabel);
        AddRow(layout, "Match timer", _matchTimerLabel);
        AddRow(layout, "Focus", CreateFocusPanel());
        AddRow(layout, "Last", _actionLabel);
        AddRow(layout, "Debug", CreateDebugPanel());

        Controls.Add(layout);

        layout.PerformLayout();
        var preferredSize = layout.GetPreferredSize(Size.Empty);
        var targetHeight = (int)Math.Ceiling(preferredSize.Height * 1.2);
        var targetSize = new Size(preferredSize.Width, targetHeight);
        ClientSize = targetSize;
        MinimumSize = targetSize;
        MaximumSize = targetSize;

        var refreshInterval = Math.Max(100, 1000 / Math.Max(1, _matchOptions.ClockExpectedHz));
        _refreshTimer = new System.Windows.Forms.Timer { Interval = refreshInterval };
        _refreshTimer.Tick += (_, _) => RenderSnapshot();
        _refreshTimer.Start();

        _focusTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _focusTimer.Tick += OnFocusTimerTick;
        _focusTimer.Start();

        _debugGameTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _debugGameTimer.Tick += OnDebugTimerTick;

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
        var panel = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };

        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _focusLabel.AutoSize = true;
        _focusLabel.AutoEllipsis = true;
        _focusLabel.MaximumSize = new Size(280, 0);

        _focusButton.AutoSize = true;
        _focusButton.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _focusButton.Margin = new Padding(8, 0, 0, 0);
        _focusButton.Text = "Focus window";
        _focusButton.Anchor = AnchorStyles.Right;
        _focusButton.Click += OnFocusButtonClick;

        panel.Controls.Add(_focusLabel, 0, 0);
        panel.Controls.Add(_focusButton, 1, 0);
        return panel;
    }

    private Control CreateDebugPanel()
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight
        };

        panel.Controls.Add(CreateDebugButton("Idle", OnDebugIdleClick));
        panel.Controls.Add(CreateDebugButton("WaitingOnStart", OnDebugWaitingOnStartClick));
        panel.Controls.Add(CreateDebugButton("Countdown", OnDebugCountdownClick));
        panel.Controls.Add(CreateDebugButton("Running", OnDebugRunningClick));
        panel.Controls.Add(CreateDebugButton("Start timer", OnDebugStartTimerClick));
        panel.Controls.Add(CreateDebugButton("Stop timer", OnDebugStopTimerClick));
        panel.Controls.Add(CreateDebugButton("WaitingOnFinalData", OnDebugWaitingOnFinalDataClick));
        panel.Controls.Add(CreateDebugButton("Completed", OnDebugCompletedClick));
        panel.Controls.Add(CreateDebugButton("Cancelled", OnDebugCancelledClick));
        panel.Controls.Add(CreateDebugButton("Debug Ctrl+S", OnDebugEndMatchClick));

        return panel;
    }

    private Button CreateDebugButton(string text, EventHandler onClick)
    {
        var button = new Button
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Text = text,
            Margin = new Padding(0, 0, 4, 0)
        };

        button.Click += onClick;
        return button;
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

    private async void OnDebugTimerTick(object? sender, EventArgs e)
    {
        if (_debugTimerStatus is null)
        {
            StopDebugTimer();
            return;
        }

        _debugElapsedSec += _debugGameTimer.Interval / 1000.0;
        var remainingMs = _debugTimerDurationSec > 0
            ? Math.Max(0, (_debugTimerDurationSec - _debugElapsedSec) * 1000)
            : (double?)null;

        await SendDebugSnapshotAsync(_debugTimerStatus.Value, _debugElapsedSec, remainingMs).ConfigureAwait(true);

        if (_debugTimerDurationSec > 0 && _debugElapsedSec >= _debugTimerDurationSec)
        {
            if (_debugTimerStatus == MatchSnapshotStatus.Countdown)
            {
                await StartDebugTimerAsync(MatchSnapshotStatus.Running, RunningDebugDurationSec).ConfigureAwait(true);
            }
            else
            {
                StopDebugTimer();
            }
        }
    }

    private async Task StartDebugTimerAsync(MatchSnapshotStatus status, double durationSec)
    {
        StopDebugTimer();
        _debugTimerStatus = status;
        _debugTimerDurationSec = durationSec;
        _debugElapsedSec = 0;

        await SendDebugSnapshotAsync(status, _debugElapsedSec, durationSec * 1000).ConfigureAwait(true);
        _debugGameTimer.Start();
    }

    private void OnDebugIdleClick(object? sender, EventArgs e)
    {
        StopDebugTimer();
        _debugElapsedSec = 0;
        _debugMatchId = null;
        _coordinator.SetIdle();
    }

    private async void OnDebugWaitingOnStartClick(object? sender, EventArgs e)
    {
        StopDebugTimer();
        _debugElapsedSec = 0;
        await SendDebugSnapshotAsync(MatchSnapshotStatus.WaitingOnStart, _debugElapsedSec).ConfigureAwait(true);
    }

    private async void OnDebugCountdownClick(object? sender, EventArgs e)
    {
        await StartDebugTimerAsync(MatchSnapshotStatus.Countdown, CountdownDebugDurationSec).ConfigureAwait(true);
    }

    private async void OnDebugRunningClick(object? sender, EventArgs e)
    {
        await StartDebugTimerAsync(MatchSnapshotStatus.Running, RunningDebugDurationSec).ConfigureAwait(true);
    }

    private async void OnDebugStartTimerClick(object? sender, EventArgs e)
    {
        await StartDebugTimerAsync(MatchSnapshotStatus.Running, _matchOptions.LtDisplayedDurationSec).ConfigureAwait(true);
    }

    private void OnDebugStopTimerClick(object? sender, EventArgs e)
    {
        StopDebugTimer();
    }

    private async void OnDebugWaitingOnFinalDataClick(object? sender, EventArgs e)
    {
        StopDebugTimer();
        await SendDebugSnapshotAsync(MatchSnapshotStatus.WaitingOnFinalData, _debugElapsedSec).ConfigureAwait(true);
    }

    private async void OnDebugCompletedClick(object? sender, EventArgs e)
    {
        StopDebugTimer();
        await SendDebugSnapshotAsync(MatchSnapshotStatus.Completed, _debugElapsedSec, isLastSend: true).ConfigureAwait(true);
    }

    private async void OnDebugCancelledClick(object? sender, EventArgs e)
    {
        StopDebugTimer();
        await SendDebugSnapshotAsync(MatchSnapshotStatus.Cancelled, _debugElapsedSec, isLastSend: true).ConfigureAwait(true);
    }

    private async void OnDebugEndMatchClick(object? sender, EventArgs e)
    {
        var button = sender as Button;
        try
        {
            if (button is not null)
            {
                button.Enabled = false;
            }

            var result = await _focusService.TryEndMatchAsync("Debug end-match shortcut", CancellationToken.None).ConfigureAwait(true);
            var description = result.FocusAcquired
                ? $"Debug shortcut succeeded: {result.Description}"
                : $"Debug shortcut failed: {result.Description}";
            _coordinator.ReportFocusResult(result.FocusAcquired, description);
            if (!result.FocusAcquired)
            {
                _logger.LogWarning("Debug shortcut failed: {Description}", result.Description);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Debug shortcut attempt threw an exception");
            _coordinator.ReportFocusResult(false, "Debug shortcut encountered an error");
        }
        finally
        {
            if (button is not null)
            {
                button.Enabled = true;
            }
        }
    }

    private void StopDebugTimer()
    {
        if (_debugGameTimer.Enabled)
        {
            _debugGameTimer.Stop();
        }

        _debugTimerStatus = null;
        _debugTimerDurationSec = 0;
    }

    private void EnsureDebugMatchId()
    {
        _debugMatchId ??= $"debug-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
    }

    private Task SendDebugSnapshotAsync(MatchSnapshotStatus status, double elapsedSeconds, double? remainingMsOverride = null, bool isLastSend = false)
    {
        EnsureDebugMatchId();
        var remainingMs = remainingMsOverride.HasValue
            ? (int)Math.Max(0, remainingMsOverride.Value)
            : (int)Math.Max(0, (_matchOptions.LtDisplayedDurationSec - elapsedSeconds) * 1000);
        var dto = new MatchSnapshotDto
        {
            Id = _debugMatchId!,
            Timestamp = DateTimeOffset.UtcNow.UtcTicks,
            IsLastSend = isLastSend,
            Status = status,
            RemainingTimeMs = remainingMs,
            WinnerTeam = null
        };

        return _coordinator.UpdateMatchSnapshotAsync(dto, CancellationToken.None);
    }

    private static string FormatTimeMs(int? remainingMs)
    {
        if (remainingMs is null)
        {
            return "—";
        }

        var totalSeconds = Math.Max(0, remainingMs.Value / 1000);
        var minutes = totalSeconds / 60;
        var seconds = totalSeconds % 60;
        return $"{minutes:0}:{seconds:00}";
    }

    private static string FormatSeconds(double seconds)
    {
        var clamped = Math.Max(0, seconds);
        var timeSpan = TimeSpan.FromSeconds(clamped);
        return $"{(int)timeSpan.TotalMinutes:0}:{timeSpan.Seconds:00}";
    }

    private static string FormatMatchTimer(MatchStateSnapshot snapshot)
    {
        if (snapshot.LifecycleState == MatchLifecycleState.Idle)
        {
            return "—";
        }

        if (snapshot.IsOvertime && snapshot.OvertimeRemainingSec is not null)
        {
            return $"OT {FormatSeconds(snapshot.OvertimeRemainingSec.Value)}";
        }

        return FormatTimeMs(snapshot.RemainingTimeMs);
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
        _propLabel.Text = snapshot.PropState.ToString();

        _plantLabel.Text = snapshot.PlantTimeSec.HasValue
            ? $"{snapshot.PlantTimeSec.Value:F1}s"
            : "—";

        _latencyLabel.Text = snapshot.LastClockLatency.HasValue
            ? $"Latency {snapshot.LastClockLatency.Value.TotalMilliseconds:F0} ms"
            : "Latency —";

        _defuseTimerLabel.Text = snapshot.IsOvertime && snapshot.OvertimeRemainingSec is not null
            ? FormatSeconds(snapshot.OvertimeRemainingSec.Value)
            : "—";

        _matchTimerLabel.Text = FormatMatchTimer(snapshot);

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
            _debugGameTimer.Dispose();
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
