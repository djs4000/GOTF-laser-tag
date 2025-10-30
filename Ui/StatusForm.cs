using System.Drawing;
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
    private readonly Timer _refreshTimer;
    private MatchStateSnapshot _snapshot = MatchStateSnapshot.Default;

    private readonly Label _matchLabel = new();
    private readonly Label _stateLabel = new();
    private readonly Label _overtimeLabel = new();
    private readonly Label _propLabel = new();
    private readonly Label _plantLabel = new();
    private readonly Label _latencyLabel = new();
    private readonly Label _focusLabel = new();
    private readonly Label _actionLabel = new();

    internal bool AllowClose { get; set; }

    public StatusForm(MatchCoordinator coordinator, IFocusService focusService, IOptions<MatchOptions> options, ILogger<StatusForm> logger)
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
            RowCount = 7,
            Padding = new Padding(10),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        AddRow(layout, "Match", _matchLabel);
        AddRow(layout, "State", CreateStatePanel());
        AddRow(layout, "Prop", _propLabel);
        AddRow(layout, "Plant", _plantLabel);
        AddRow(layout, "Clock", _latencyLabel);
        AddRow(layout, "Focus", _focusLabel);
        AddRow(layout, "Last", _actionLabel);

        Controls.Add(layout);

        var refreshInterval = Math.Max(100, 1000 / Math.Max(1, options.Value.ClockExpectedHz));
        _refreshTimer = new Timer { Interval = refreshInterval };
        _refreshTimer.Tick += (_, _) => RenderSnapshot();
        _refreshTimer.Start();

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
        _propLabel.Text = snapshot.PropState.ToString();

        _plantLabel.Text = snapshot.PlantTimeSec.HasValue
            ? $"{snapshot.PlantTimeSec.Value:F1}s"
            : "—";

        _latencyLabel.Text = snapshot.LastClockLatency.HasValue
            ? $"Latency {snapshot.LastClockLatency.Value.TotalMilliseconds:F0} ms"
            : "Latency —";

        _focusLabel.Text = snapshot.FocusAcquired ? "Target in focus" : "Awaiting focus";
        _focusLabel.ForeColor = snapshot.FocusAcquired ? Color.DarkGreen : Color.DarkRed;

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
}
