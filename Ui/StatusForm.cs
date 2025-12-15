using System;
using System.Collections.Generic;
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
    private readonly ToolbarNavigationService _toolbarNavigationService;
    private readonly MatchOptions _matchOptions;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly System.Windows.Forms.Timer _focusTimer;
    private readonly System.Windows.Forms.Timer _debugGameTimer;
    private MatchStateSnapshot _snapshot = MatchStateSnapshot.Default;
    private FocusWindowInfo _focusWindowInfo = FocusWindowInfo.Empty;
    private MatchStateSnapshot? _lastRenderedSnapshot;
    private bool _hasShownResults;

    private readonly Label _matchLabel = new();
    private readonly Label _httpLabel = new();
    private readonly Label _stateLabel = new();
    private readonly Label _overtimeLabel = new();
    private readonly Label _relayStatusLabel = new();
    private readonly Label _propLabel = new();
    private readonly Label _plantLabel = new();
    private readonly Label _matchLatencyLabel = new();
    private readonly Label _propLatencyLabel = new();
    private readonly Label _defuseTimerLabel = new();
    private readonly Label _matchTimerLabel = new();
    private readonly Label _playerCountsLabel = new();
    private readonly Label _focusLabel = new();
    private readonly Label _actionLabel = new();
    private readonly ComboBox _attackingTeamComboBox = new();
    private readonly Label _teamNamesCheckLabel = new();
    private readonly Label _playerNamesCheckLabel = new();
    private readonly Label _matchDurationNoticeLabel = new();
    private readonly Button _focusButton = new();
    private ToolStripButton? _settingsToolbarButton;
    private ToolStripButton? _relayToolbarButton;
    private ToolStripButton? _debugToolbarButton;
    private const double CountdownDebugDurationSec = 5;
    private const double RunningDebugDurationSec = 219;
    // Layout guardrails: keep StatusForm contents fully visible at 1280x720,
    // stack panels gracefully when narrower, and respect DPI scaling.
    private const int BasePadding = 8;
    private const int DefaultClientWidth = 1280;
    private const int DefaultClientHeight = 720;
    private const int MinimumClientWidth = 900;
    private const int MinimumClientHeight = 600;
    private const int StackThresholdWidth = 1100;
    private double _debugElapsedSec;
    private double _debugTimerDurationSec;
    private MatchSnapshotStatus? _debugTimerStatus;
    private string? _debugMatchId;
    private float _layoutScale = 1f;
    private TableLayoutPanel? _contentLayout;
    private Control? _gameConfigurationContainer;
    private bool _isStackedLayout;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool AllowClose { get; set; }

    public StatusForm(
        MatchCoordinator coordinator,
        IFocusService focusService,
        ToolbarNavigationService toolbarNavigationService,
        IOptions<MatchOptions> options,
        HttpEndpointMetadata endpointMetadata,
        ILogger<StatusForm> logger)
    {
        _coordinator = coordinator;
        _logger = logger;
        _focusService = focusService;
        _toolbarNavigationService = toolbarNavigationService;
        _matchOptions = options.Value;

        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        //ShowInTaskbar = false;
        Text = "ICE Defusal Monitor";
        _layoutScale = DeviceDpi / 96f;
        Size = new Size(DefaultClientWidth, DefaultClientHeight);
        MinimumSize = new Size(MinimumClientWidth, MinimumClientHeight);
        AutoSize = false;
        AutoScroll = false;
        AutoSizeMode = AutoSizeMode.GrowOnly;

        // Toolbar insertion point (US1/T007): a ToolStrip will dock above this layout so its buttons
        // stay aligned with the always-on-top window before additional utility forms launch.
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 3,
            Padding = BuildPadding(),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        ConfigureHttpLabel(endpointMetadata);

        var httpSection = CreateSectionPanel("Listening IPs");
        var httpRow = 0;
        httpRow = AddRow(httpSection.panel, httpRow, "Endpoints", _httpLabel);
        layout.Controls.Add(httpSection.container, 0, 0);
        layout.SetColumnSpan(httpSection.container, 3);

        var matchSection = CreateSectionPanel("Match");
        var matchRow = 0;
        matchRow = AddRow(matchSection.panel, matchRow, "Match ID", _matchLabel);
        matchRow = AddRow(matchSection.panel, matchRow, "State", CreateStatePanel());
        _playerCountsLabel.MaximumSize = new Size(220, 0);
        matchRow = AddRow(matchSection.panel, matchRow, "Players", _playerCountsLabel);
        matchRow = AddRow(matchSection.panel, matchRow, "Timer", _matchTimerLabel);
        matchRow = AddRow(matchSection.panel, matchRow, "Latency", _matchLatencyLabel);
        layout.Controls.Add(matchSection.container, 0, 1);

        var propSection = CreateSectionPanel("Prop");
        var propRow = 0;
        propRow = AddRow(propSection.panel, propRow, "State", _propLabel);
        propRow = AddRow(propSection.panel, propRow, "Timer", _defuseTimerLabel);
        propRow = AddRow(propSection.panel, propRow, "Latency", _propLatencyLabel);
        layout.Controls.Add(propSection.container, 1, 1);

        var configSection = CreateSectionPanel("Game configuration");
        var configRow = 0;
        ConfigureAttackingTeamComboBox();
        var preflightPanel = CreatePreflightPanel();
        configRow = AddRow(configSection.panel, configRow, "Attacking team", _attackingTeamComboBox);
        configRow = AddRow(configSection.panel, configRow, "Pre-flight", preflightPanel);
        configSection.container.MinimumSize = new Size(Scale(220), 0);
        configSection.container.MaximumSize = new Size(Scale(360), 0);
        configSection.container.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        configSection.container.MinimumSize = new Size(Scale(220), 0);
        configSection.container.MaximumSize = new Size(Scale(360), 0);
        configSection.container.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        layout.Controls.Add(configSection.container, 2, 1);

        var toolbar = CreateToolbar();
        var rootLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2
        };
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Dock = DockStyle.Fill;
        rootLayout.Controls.Add(toolbar, 0, 0);
        rootLayout.Controls.Add(layout, 0, 1);

        Controls.Add(rootLayout);
        _contentLayout = layout;
        _gameConfigurationContainer = configSection.container;

        layout.PerformLayout();
        // Enforce baseline window dimensions so Match/Prop/Game Configuration panels
        // remain visible even before responsive stacking engages.
        ClientSize = new Size(DefaultClientWidth, DefaultClientHeight);
        MinimumSize = new Size(MinimumClientWidth, MinimumClientHeight);
        UpdateLayoutMode();

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

        _relayStatusLabel.AutoSize = true;
        _relayStatusLabel.Margin = new Padding(8, 0, 0, 0);
        _relayStatusLabel.ForeColor = Color.DimGray;
        _relayStatusLabel.AutoEllipsis = true;
        _relayStatusLabel.MaximumSize = new Size(Scale(360), 0);

        panel.Controls.Add(_stateLabel);
        panel.Controls.Add(_overtimeLabel);
        panel.Controls.Add(_relayStatusLabel);
        return panel;
    }

    private void ConfigureAttackingTeamComboBox()
    {
        _attackingTeamComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _attackingTeamComboBox.Items.Clear();
        _attackingTeamComboBox.Items.AddRange(new object[] { "Team 1", "Team 2" });
        _attackingTeamComboBox.SelectedIndexChanged += (_, _) =>
        {
            var selected = _attackingTeamComboBox.SelectedItem as string;
            _coordinator.AttackingTeam = string.IsNullOrWhiteSpace(selected) ? "Team 1" : selected!;
        };
        if (_attackingTeamComboBox.Items.Count > 0)
        {
            _attackingTeamComboBox.SelectedIndex = 0;
        }
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

    private Control CreatePreflightPanel()
    {
        var panel = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };

        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        ConfigureChecklistLabel(_teamNamesCheckLabel);
        ConfigureChecklistLabel(_playerNamesCheckLabel);
        _matchDurationNoticeLabel.AutoSize = true;
        _matchDurationNoticeLabel.MaximumSize = new Size(Scale(320), 0);
        _matchDurationNoticeLabel.AutoEllipsis = true;
        _matchDurationNoticeLabel.Margin = new Padding(0, 3, 0, 3);
        _matchDurationNoticeLabel.ForeColor = Color.DimGray;

        var row = 0;
        row = AddChecklistRow(panel, row, _teamNamesCheckLabel);
        row = AddChecklistRow(panel, row, _playerNamesCheckLabel);
        AddChecklistRow(panel, row, _matchDurationNoticeLabel);

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

    private (GroupBox container, TableLayoutPanel panel) CreateSectionPanel(string title)
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

        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var groupBox = new GroupBox
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Margin = new Padding(Scale(BasePadding)),
            Padding = BuildPadding(),
            Text = title
        };

        groupBox.Controls.Add(panel);
        panel.RowCount = 0;
        return (groupBox, panel);
    }

    private static int AddRow(TableLayoutPanel layout, int rowIndex, string label, Control control)
    {
        layout.RowCount = Math.Max(layout.RowCount, rowIndex + 1);
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = new Label
        {
            Text = label + ":",
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(0, 3, 8, 3)
        };

        control.AutoSize = true;
        control.Margin = new Padding(0, 3, 0, 3);
        layout.Controls.Add(header, 0, rowIndex);
        layout.Controls.Add(control, 1, rowIndex);
        return rowIndex + 1;
    }

    private void ConfigureChecklistLabel(Label label)
    {
        label.AutoSize = true;
        label.MaximumSize = new Size(Scale(280), 0);
        label.AutoEllipsis = true;
        label.Margin = new Padding(0, 3, 0, 3);
    }

    private static int AddChecklistRow(TableLayoutPanel panel, int rowIndex, Control control)
    {
        panel.RowCount = Math.Max(panel.RowCount, rowIndex + 1);
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(control, 0, rowIndex);
        return rowIndex + 1;
    }

    private Padding BuildPadding(int multiplier = 1)
    {
        var scaled = Scale(BasePadding * multiplier);
        return new Padding(scaled);
    }

    private int Scale(int value)
    {
        return (int)Math.Round(value * _layoutScale, MidpointRounding.AwayFromZero);
    }

    private void OnSnapshotUpdated(object? sender, MatchStateSnapshot snapshot)
    {
        _snapshot = snapshot;
        _lastRenderedSnapshot = null; // Force a full re-render on next tick
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

    private static string FormatLatency(LatencySampleSnapshot? latency)
    {
        if (latency is null)
        {
            return "Latency —";
        }

        return $"Latency avg {latency.Average.TotalMilliseconds:F0} ms (min {latency.Minimum.TotalMilliseconds:F0} / max {latency.Maximum.TotalMilliseconds:F0})";
    }

    private static string FormatPlayerCounts(IReadOnlyList<TeamPlayerCountSnapshot> counts)
    {
        if (counts.Count == 0)
        {
            return "—";
        }

        var lines = counts
            .Select(count => $"{count.Team}: {count.Alive}/{count.Total} alive")
            .ToArray();

        return string.Join(Environment.NewLine, lines);
    }

    private static double? CalculatePropTimerRemainingSeconds(MatchStateSnapshot snapshot)
    {
        if (snapshot.PropTimerRemainingMs is null)
        {
            return null;
        }

        if (snapshot.PropState != PropState.Armed || snapshot.PropTimerSyncedAt is null)
        {
            return snapshot.PropTimerRemainingMs.Value / 1000.0;
        }

        var elapsedMs = (DateTimeOffset.UtcNow - snapshot.PropTimerSyncedAt.Value).TotalMilliseconds;
        var remainingMs = snapshot.PropTimerRemainingMs.Value - elapsedMs;
        return Math.Max(0, remainingMs) / 1000.0;
    }

    private static bool? AreTeamNamesValid(IReadOnlyList<MatchPlayerSnapshotDto> players)
    {
        if (players.Count == 0)
        {
            return null;
        }

        var expectedTeams = new HashSet<string>(new[] { "Team 1", "Team 2" }, StringComparer.OrdinalIgnoreCase);
        var observedTeams = players
            .Select(player => player.Team)
            .Where(team => !string.IsNullOrWhiteSpace(team))
            .Select(team => team.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (observedTeams.Count != expectedTeams.Count)
        {
            return false;
        }

        return expectedTeams.SetEquals(observedTeams);
    }

    private static bool? ArePlayerNamesValid(IReadOnlyList<MatchPlayerSnapshotDto> players)
    {
        if (players.Count == 0)
        {
            return null;
        }

        foreach (var player in players)
        {
            if (string.IsNullOrWhiteSpace(player.Id) || string.IsNullOrWhiteSpace(player.Team))
            {
                return false;
            }

            if (!IsPlayerNameAlignedWithTeam(player.Id, player.Team))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsPlayerNameAlignedWithTeam(string playerName, string teamName)
    {
        var trimmedTeam = teamName.Trim();
        if (!playerName.StartsWith(trimmedTeam, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var suffix = playerName[trimmedTeam.Length..].Trim();
        return suffix.Length == 1 && suffix[0] is >= 'A' and <= 'Z';
    }

    private void UpdatePreflightChecks(MatchStateSnapshot snapshot)
    {
        var teamNamesValid = AreTeamNamesValid(snapshot.Players);
        SetChecklistLabel(_teamNamesCheckLabel, teamNamesValid, "Team names set to Team 1 and Team 2", "Team names should be Team 1 and Team 2");

        var playerNamesValid = ArePlayerNamesValid(snapshot.Players);

        SetChecklistLabel(_playerNamesCheckLabel, playerNamesValid, "Players named Team X + letter", "Players should be named Team 1 A, Team 1 B, Team 2 A?");



        var configuredText = FormatSeconds(_matchOptions.PreflightExpectedMatchLengthSec);

        _matchDurationNoticeLabel.Text = $"Configured length: {configuredText}. Preflight waits for host data before showing countdown info.";
    }

    private static void SetChecklistLabel(Label label, bool? passed, string successText, string failureText)
    {
        if (passed is null)
        {
            label.Text = "… Waiting for roster";
            label.ForeColor = Color.DimGray;
            return;
        }

        if (passed.Value)
        {
            label.Text = "✓ " + successText;
            label.ForeColor = Color.DarkGreen;
        }
        else
        {
            label.Text = "⚠ " + failureText;
            label.ForeColor = Color.DarkRed;
        }
    }

    private void UpdateRelayStatus(MatchStateSnapshot snapshot)
    {
        if (!snapshot.RelayEnabled)
        {
            _relayStatusLabel.Text = "Relay disabled";
            _relayStatusLabel.ForeColor = Color.DimGray;
            return;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.RelayError))
        {
            _relayStatusLabel.Text = $"Relay error: {snapshot.RelayError}";
            _relayStatusLabel.ForeColor = Color.DarkRed;
            return;
        }

        if (snapshot.RelaySending)
        {
            _relayStatusLabel.Text = "Relay active";
            _relayStatusLabel.ForeColor = Color.DarkGreen;
            return;
        }

        _relayStatusLabel.Text = "Relay ready";
        _relayStatusLabel.ForeColor = Color.DarkOrange;
    }

    private void RenderSnapshot()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        var snapshot = _snapshot;

        if (ReferenceEquals(snapshot, _lastRenderedSnapshot))
        {
            UpdateLiveTimers(snapshot);
            return;
        }

        var previousState = _lastRenderedSnapshot?.LifecycleState;
        var currentState = snapshot.LifecycleState;

        if (currentState is MatchLifecycleState.Idle or MatchLifecycleState.WaitingOnStart)
        {
            _hasShownResults = false;
        }

        var isTerminal = currentState is MatchLifecycleState.Completed or MatchLifecycleState.Cancelled;
        var wasTerminal = previousState is MatchLifecycleState.Completed or MatchLifecycleState.Cancelled;

        if (isTerminal && !wasTerminal && !_hasShownResults)
        {
            _hasShownResults = true;
            ShowMatchResults(snapshot);
        }

        _matchLabel.Text = snapshot.MatchId ?? "—";
        var hasMatchData = snapshot.MatchId is not null
            || snapshot.LastClockUpdate is not null
            || snapshot.LifecycleState != MatchLifecycleState.Idle;

        _stateLabel.Text = hasMatchData
            ? snapshot.LifecycleState.ToString()
            : "—";

        UpdateRelayStatus(snapshot);

        var hasPropData = snapshot.LastPropUpdate is not null
            || snapshot.PropTimerRemainingMs is not null
            || snapshot.PropState != PropState.Idle;

        _propLabel.Text = hasPropData
            ? snapshot.PropState.ToString()
            : "—";

        _plantLabel.Text = snapshot.PlantTimeSec.HasValue
            ? $"{snapshot.PlantTimeSec.Value:F1}s"
            : "—";

        _matchLatencyLabel.Text = FormatLatency(snapshot.ClockLatency);

        _playerCountsLabel.Text = FormatPlayerCounts(snapshot.TeamPlayerCounts);

        _propLatencyLabel.Text = FormatLatency(snapshot.PropLatency);

        var propTimerRemainingSec = CalculatePropTimerRemainingSeconds(snapshot);
        if (propTimerRemainingSec is not null)
        {
            _defuseTimerLabel.Text = $"{propTimerRemainingSec.Value:F1}s";
        }
        else if (snapshot.IsOvertime && snapshot.OvertimeRemainingSec is not null)
        {
            _defuseTimerLabel.Text = $"{Math.Max(0, snapshot.OvertimeRemainingSec.Value):F1}s";
        }
        else
        {
            _defuseTimerLabel.Text = "—";
        }
        UpdateLiveTimers(snapshot);

        _matchTimerLabel.Text = FormatMatchTimer(snapshot);

        UpdateFocusLabel();

        UpdatePreflightChecks(snapshot);

        _actionLabel.Text = snapshot.LastActionDescription;

        _attackingTeamComboBox.Enabled = snapshot.LifecycleState is not (MatchLifecycleState.Countdown or MatchLifecycleState.Running);

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

        _lastRenderedSnapshot = snapshot;
    }

    private void UpdateLiveTimers(MatchStateSnapshot snapshot)
    {
        var propTimerRemainingSec = CalculatePropTimerRemainingSeconds(snapshot);
        if (propTimerRemainingSec is not null)
        {
            _defuseTimerLabel.Text = $"{propTimerRemainingSec.Value:F1}s";
        }
        else if (snapshot.IsOvertime && snapshot.OvertimeRemainingSec is not null)
        {
            _defuseTimerLabel.Text = $"{Math.Max(0, snapshot.OvertimeRemainingSec.Value):F1}s";
        }
        else
        {
            _defuseTimerLabel.Text = "—";
        }

        _matchTimerLabel.Text = FormatMatchTimer(snapshot);

        _overtimeLabel.Visible = snapshot.IsOvertime && snapshot.OvertimeRemainingSec is not null;
        _overtimeLabel.Text = snapshot.IsOvertime && snapshot.OvertimeRemainingSec is not null ? $"OVERTIME {Math.Max(0, snapshot.OvertimeRemainingSec.Value):F1}s" : "OVERTIME";
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

    protected override void OnCreateControl()
    {
        base.OnCreateControl();
        _layoutScale = DeviceDpi / 96f;
        ApplyScaledLayoutGuidelines();
        UpdateLayoutMode();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateLayoutMode();
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

    private void ShowMatchResults(MatchStateSnapshot snapshot)
    {
        var resultForm = new MatchResultForm(snapshot, _coordinator.AttackingTeam, _coordinator.DefendingTeam);
        resultForm.Show(this);
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

    private ToolStrip CreateToolbar()
    {
        var strip = new ToolStrip
        {
            Dock = DockStyle.Top,
            GripStyle = ToolStripGripStyle.Hidden,
            Stretch = true,
            RenderMode = ToolStripRenderMode.System
        };

        _settingsToolbarButton = new ToolStripButton("&Settings")
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            ToolTipText = "Open Settings (Alt+S)"
        };
        _settingsToolbarButton.Click += OnSettingsToolbarClick;

        _relayToolbarButton = new ToolStripButton("&Relay Monitor")
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            ToolTipText = "Open Relay Monitor (Alt+R)"
        };
        _relayToolbarButton.Click += OnRelayMonitorToolbarClick;

        _debugToolbarButton = new ToolStripButton("&Debugging")
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            ToolTipText = "Open Debug Payload Injector (Alt+D)"
        };
        _debugToolbarButton.Click += OnDebugToolbarClick;

        strip.Items.Add(_settingsToolbarButton);
        strip.Items.Add(new ToolStripSeparator());
        strip.Items.Add(_relayToolbarButton);
        strip.Items.Add(new ToolStripSeparator());
        strip.Items.Add(_debugToolbarButton);
        return strip;
    }

    private void OnSettingsToolbarClick(object? sender, EventArgs e)
    {
        try
        {
            _toolbarNavigationService.ShowOwnedForm<SettingsForm>(this);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open Settings form via toolbar.");
            MessageBox.Show(this, "Unable to open the Settings window. Check logs for details.", "Toolbar", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnRelayMonitorToolbarClick(object? sender, EventArgs e)
    {
        try
        {
            _toolbarNavigationService.ShowOwnedForm<RelayMonitorForm>(this);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open Relay Monitor.");
            MessageBox.Show(this, "Unable to open the Relay Monitor. Check logs for details.", "Toolbar", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnDebugToolbarClick(object? sender, EventArgs e)
    {
        try
        {
            _toolbarNavigationService.ShowOwnedForm<DebugPayloadForm>(this);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open Debug Payload form.");
            MessageBox.Show(this, "Unable to open the Debug Payload Injector. Check logs for details.", "Toolbar", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Ensures Match/Prop/Game Configuration panels either render side-by-side or stack vertically when the window shrinks.
    /// </summary>
    private void UpdateLayoutMode()
    {
        if (_contentLayout is null || _gameConfigurationContainer is null)
        {
            return;
        }

        var shouldStack = ClientSize.Width < StackThresholdWidth;
        if (_isStackedLayout == shouldStack)
        {
            return;
        }

        _isStackedLayout = shouldStack;
        if (shouldStack)
        {
            _contentLayout.SetColumnSpan(_gameConfigurationContainer, 3);
            _contentLayout.SetCellPosition(_gameConfigurationContainer, new TableLayoutPanelCellPosition(0, 2));
        }
        else
        {
            _contentLayout.SetColumnSpan(_gameConfigurationContainer, 1);
            _contentLayout.SetCellPosition(_gameConfigurationContainer, new TableLayoutPanelCellPosition(2, 1));
        }
    }

    /// <summary>
    /// Reapplies padding and label constraints whenever the DPI scale changes.
    /// </summary>
    private void ApplyScaledLayoutGuidelines()
    {
        if (_contentLayout is null)
        {
            return;
        }

        _contentLayout.Padding = BuildPadding();
        foreach (var group in _contentLayout.Controls.OfType<GroupBox>())
        {
            group.Padding = BuildPadding();
        }
        ConfigureChecklistLabel(_teamNamesCheckLabel);
        ConfigureChecklistLabel(_playerNamesCheckLabel);
        _matchDurationNoticeLabel.MaximumSize = new Size(Scale(320), 0);
        _relayStatusLabel.MaximumSize = new Size(Scale(360), 0);
    }
}
