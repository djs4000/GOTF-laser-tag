using System;
using System.Collections.Generic;
using System.Linq;
using LaserTag.Defusal.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LaserTag.Defusal.Services;

/// <summary>
/// Central orchestrator that consumes prop and clock updates, evaluates match-ending conditions,
/// and issues UI automation commands when required.
/// </summary>
public sealed class MatchCoordinator
{
    private readonly ILogger<MatchCoordinator> _logger;
    private readonly MatchOptions _matchOptions;
    private readonly RelayService _relayService;
    private readonly IFocusService _focusService;
    private readonly UiAutomationOptions _uiAutomationOptions;
    private readonly TimeSynchronizationService _timeSyncService;
    private readonly object _sync = new();

    private MatchLifecycleState _lifecycleState = MatchLifecycleState.Idle;
    private PropState _propState = PropState.Idle;
    private double? _plantTimeSec;
    private bool _matchEnded;
    private string? _currentMatchId;
    private long _lastPropTimestamp;
    private long _lastSnapshotTimestamp;
    private int? _lastMatchRemainingMs;
    private double _lastElapsedSec;
    private DateTimeOffset? _lastClockUpdate;
    private DateTimeOffset? _lastPropUpdate;
    private readonly Queue<TimeSpan> _propLatencyWindow = new();
    private readonly Queue<TimeSpan> _clockLatencyWindow = new();
    private int _propLatencySamplesUntilPublish;
    private int _clockLatencySamplesUntilPublish;
    private LatencySampleSnapshot? _propLatency;
    private LatencySampleSnapshot? _clockLatency;
    private string _lastActionDescription = "Idle";
    private bool _focusAcquired;
    private DateTimeOffset _lastAutomationAt = DateTimeOffset.MinValue;
    private PropStatusDto? _lastPropPayload;
    private MatchSnapshotDto? _lastSnapshotPayload;
    private double? _propTimerRemainingMs;
    private DateTimeOffset? _propTimerSyncedAt;
    private string _attackingTeam = "Team 1";

    public event EventHandler<MatchStateSnapshot>? SnapshotUpdated;

    public MatchStateSnapshot CurrentSnapshot { get; private set; } = MatchStateSnapshot.Default;

    public int? LastKnownRemainingTimeMs => _lastMatchRemainingMs;

    public string AttackingTeam
    {
        get
        {
            lock (_sync)
            {
                return _attackingTeam;
            }
        }
        set
        {
            lock (_sync)
            {
                _attackingTeam = string.Equals(value, "Team 2", StringComparison.OrdinalIgnoreCase) ? "Team 2" : "Team 1";
            }
        }
    }

    public string DefendingTeam
    {
        get
        {
            lock (_sync)
            {
                return string.Equals(_attackingTeam, "Team 1", StringComparison.Ordinal) ? "Team 2" : "Team 1";
            }
        }
    }

    public MatchCoordinator(
        IOptions<MatchOptions> matchOptions,
        RelayService relayService,
        IFocusService focusService,
        IOptions<UiAutomationOptions> uiAutomationOptions,
        TimeSynchronizationService timeSyncService,
        ILogger<MatchCoordinator> logger)
    {
        _relayService = relayService;
        _focusService = focusService;
        _uiAutomationOptions = uiAutomationOptions.Value;
        _timeSyncService = timeSyncService;
        _logger = logger;
        _matchOptions = matchOptions.Value;
    }

    /// <summary>
    /// Applies a new prop status update.
    /// </summary>
    public async Task<MatchStateSnapshot> UpdatePropAsync(PropStatusDto dto, CancellationToken cancellationToken)
    {
        bool shouldTriggerEnd = false;
        string? triggerReason = null;
        PropState incomingState;
        var normalizedTime = _timeSyncService.NormalizePropTime(dto.Timestamp, dto.UptimeMs);
        var normalizedTimestampMs = normalizedTime.ToUnixTimeMilliseconds();

        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            var observedLatency = now - normalizedTime;
            var clampedLatency = observedLatency < TimeSpan.Zero ? TimeSpan.Zero : observedLatency;
            var updatedLatency = UpdateLatencyMetrics(_propLatencyWindow, clampedLatency, ref _propLatencySamplesUntilPublish);
            if (updatedLatency is not null)
            {
                _propLatency = updatedLatency;
            }

            if (_matchEnded && IsTerminalState(_lifecycleState))
            {
                return CurrentSnapshot;
            }

            if (normalizedTimestampMs < _lastPropTimestamp)
            {
                _logger.LogWarning("Out-of-order prop payload ignored (timestamp {Timestamp})", normalizedTimestampMs);
                return CurrentSnapshot;
            }

            incomingState = dto.State;
            _lastPropTimestamp = normalizedTimestampMs;
            _lastPropUpdate = now;
            _lastPropPayload = dto;
            _propState = incomingState;

            if (dto.TimerMs is not null)
            {
                _propTimerRemainingMs = Math.Max(0, dto.TimerMs.Value);
                _propTimerSyncedAt = normalizedTime;
            }

            if (_lifecycleState is MatchLifecycleState.Idle or MatchLifecycleState.WaitingOnStart or MatchLifecycleState.Countdown)
            {
                PublishSnapshotLocked("Prop update (inactive match)");
                return CurrentSnapshot;
            }

            if (IsPlantState(incomingState))
            {
                if (_plantTimeSec is null || Math.Abs(_plantTimeSec.Value - _lastElapsedSec) > double.Epsilon)
                {
                    _plantTimeSec = _lastElapsedSec;
                    _logger.LogInformation("Bomb planted at {Elapsed:F1}s", _plantTimeSec);
                }
            }

            if (IsTerminalPropState(incomingState))
            {
                shouldTriggerEnd = true;
                triggerReason = incomingState == PropState.Defused ? "Prop defused" : "Prop detonated";
                _matchEnded = true;
            }

            PublishSnapshotLocked("Prop update");
        }

        if (shouldTriggerEnd)
        {
            await TriggerEndMatchAsync(triggerReason!, cancellationToken).ConfigureAwait(false);
        }

        return CurrentSnapshot;
    }

    /// <summary>
    /// Builds the response payload expected by the prop, reflecting the latest known match state.
    /// </summary>
    public PropUpdateResponseDto BuildPropResponse(long requestTimestamp)
    {
        lock (_sync)
        {
            var status = CurrentSnapshot.LifecycleState.ToString();
            var remaining = CurrentSnapshot.RemainingTimeMs ?? _lastMatchRemainingMs ?? 0;
            var timestamp = _lastSnapshotTimestamp != 0
                ? _lastSnapshotTimestamp
                : _lastPropTimestamp != 0
                    ? _lastPropTimestamp
                    : requestTimestamp;

            return new PropUpdateResponseDto
            {
                Status = status,
                RemainingTimeMs = remaining,
                Timestamp = timestamp
            };
        }
    }

    /// <summary>
    /// Applies a new match snapshot update.
    /// </summary>
    public async Task<MatchStateSnapshot> UpdateMatchSnapshotAsync(MatchSnapshotDto dto, CancellationToken cancellationToken)
    {
        bool shouldTriggerEnd = false;
        string? triggerReason = null;

        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            var sourceTimestamp = ParseSnapshotTimestamp(dto.Timestamp, now);
            var observedLatency = now - sourceTimestamp;
            var clampedLatency = observedLatency < TimeSpan.Zero ? TimeSpan.Zero : observedLatency;
            var updatedLatency = UpdateLatencyMetrics(_clockLatencyWindow, clampedLatency, ref _clockLatencySamplesUntilPublish);
            if (updatedLatency is not null)
            {
                _clockLatency = updatedLatency;
            }

            if (string.IsNullOrWhiteSpace(dto.Id))
            {
                _logger.LogWarning("Ignoring match snapshot with missing id while tracking {MatchId}", _currentMatchId ?? "<none>");
                return CurrentSnapshot;
            }

            var isNewMatchId = !string.Equals(_currentMatchId, dto.Id, StringComparison.Ordinal);
            if (_currentMatchId is null)
            {
                ResetForNewMatch(dto.Id);
            }
            else if (isNewMatchId)
            {
                var isTerminal = _matchEnded && IsTerminalState(_lifecycleState);
                var isStartingState = dto.Status is MatchSnapshotStatus.WaitingOnStart or MatchSnapshotStatus.Countdown or MatchSnapshotStatus.Running;

                if (!isTerminal)
                {
                    _logger.LogWarning("Ignoring snapshot for unexpected match {IncomingId} while tracking {CurrentMatchId}", dto.Id, _currentMatchId ?? "<none>");
                    return CurrentSnapshot;
                }

                if (!isStartingState)
                {
                    _logger.LogInformation("Ignoring terminal snapshot for new match {IncomingId} while {CurrentMatchId} is ended", dto.Id, _currentMatchId);
                    return CurrentSnapshot;
                }

                ResetForNewMatch(dto.Id);
            }

            if (_matchEnded && IsTerminalState(_lifecycleState) && !IsTerminalStatus(dto.Status))
            {
                return CurrentSnapshot;
            }

            if (dto.Timestamp < _lastSnapshotTimestamp)
            {
                _logger.LogWarning("Out-of-order match payload ignored (timestamp {Timestamp})", dto.Timestamp);
                return CurrentSnapshot;
            }

            _lastSnapshotTimestamp = dto.Timestamp;
            _lastClockUpdate = now;
            _lastSnapshotPayload = dto;
            _lastMatchRemainingMs = dto.RemainingTimeMs;

            _lastElapsedSec = _matchOptions.LtDisplayedDurationSec - (dto.RemainingTimeMs / 1000.0);
            if (_lastElapsedSec < 0)
            {
                _lastElapsedSec = 0;
            }

            switch (dto.Status)
            {
                case MatchSnapshotStatus.WaitingOnStart:
                    _matchEnded = false;
                    _lifecycleState = MatchLifecycleState.WaitingOnStart;
                    _plantTimeSec = null;
                    break;
                case MatchSnapshotStatus.Countdown:
                    _matchEnded = false;
                    _lifecycleState = MatchLifecycleState.Countdown;
                    _plantTimeSec = null;
                    break;
                case MatchSnapshotStatus.Running:
                    if (!_matchEnded)
                    {
                        _matchEnded = false;
                    }
                    if (_propState == PropState.Idle)
                    {
                        _propState = PropState.Active;
                    }
                    _lifecycleState = MatchLifecycleState.Running;
                    EvaluateLiveState(ref shouldTriggerEnd, ref triggerReason);
                    break;
                case MatchSnapshotStatus.WaitingOnFinalData:
                    _lifecycleState = MatchLifecycleState.WaitingOnFinalData;
                    _matchEnded = true;
                    break;
                case MatchSnapshotStatus.Completed:
                    _lifecycleState = MatchLifecycleState.Completed;
                    _matchEnded = true;
                    break;
                case MatchSnapshotStatus.Cancelled:
                    _lifecycleState = MatchLifecycleState.Cancelled;
                    _matchEnded = true;
                    break;
            }

            if (dto.IsLastSend)
            {
                _matchEnded = true;
            }

            PublishSnapshotLocked("Match update");
        }

        if (shouldTriggerEnd)
        {
            await TriggerEndMatchAsync(triggerReason!, cancellationToken).ConfigureAwait(false);
        }

        return CurrentSnapshot;
    }

    /// <summary>
    /// Records the outcome of a focus attempt for UI display.
    /// </summary>
    internal void ReportFocusResult(bool success, string actionDescription)
    {
        lock (_sync)
        {
            _focusAcquired = success;
            _lastActionDescription = actionDescription;
            PublishSnapshotLocked("Focus result");
        }
    }

    /// <summary>
    /// Provides a defensive copy of the current snapshot without subscription.
    /// </summary>
    public MatchStateSnapshot Snapshot() => CurrentSnapshot;

    /// <summary>
    /// Allows the operator to start a manual match session for diagnostics.
    /// </summary>
    public void StartManualMatch(string? matchId = null)
    {
        lock (_sync)
        {
            ResetForNewMatch(matchId ?? $"manual-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}");
            _propState = PropState.Active;
            _lifecycleState = MatchLifecycleState.Running;
            PublishSnapshotLocked("Manual start");
        }
    }

    /// <summary>
    /// Returns the coordinator to an idle state when no match data is available.
    /// </summary>
    public void SetIdle()
    {
        lock (_sync)
        {
            _currentMatchId = null;
            _propState = PropState.Idle;
            _plantTimeSec = null;
            _matchEnded = false;
            _lifecycleState = MatchLifecycleState.Idle;
            _lastPropTimestamp = 0;
            _lastSnapshotTimestamp = 0;
            _lastElapsedSec = 0;
            _lastMatchRemainingMs = null;
            _lastClockUpdate = null;
            _lastPropUpdate = null;
            _propLatencyWindow.Clear();
            _clockLatencyWindow.Clear();
            _propLatencySamplesUntilPublish = 0;
            _clockLatencySamplesUntilPublish = 0;
            _propLatency = null;
            _clockLatency = null;
            _lastActionDescription = "Idle (no match data)";
            _focusAcquired = false;
            _lastPropPayload = null;
            _lastSnapshotPayload = null;
            _propTimerRemainingMs = null;
            _propTimerSyncedAt = null;
            PublishSnapshotLocked("Idle state");
        }
    }

    /// <summary>
    /// Allows the operator to manually trigger the end-match automation.
    /// </summary>
    public Task ForceEndMatchAsync(string reason, CancellationToken cancellationToken)
    {
        return TriggerEndMatchAsync(reason, cancellationToken);
    }

    private void EvaluateLiveState(ref bool shouldTriggerEnd, ref string? triggerReason)
    {
        if (_matchEnded)
        {
            return;
        }

        if (IsTerminalPropState(_propState))
        {
            shouldTriggerEnd = true;
            triggerReason = _propState == PropState.Defused ? "Prop defused" : "Prop detonated";
            _matchEnded = true;
            return;
        }

        if (_plantTimeSec is null && _lastElapsedSec >= _matchOptions.AutoEndNoPlantAtSec)
        {
            shouldTriggerEnd = true;
            triggerReason = $"No plant by {_matchOptions.AutoEndNoPlantAtSec}s";
            _matchEnded = true;
            return;
        }

        if (_plantTimeSec is not null && _plantTimeSec.Value >= _matchOptions.AutoEndNoPlantAtSec)
        {
            var overtimeRemaining = (_plantTimeSec.Value + _matchOptions.DefuseWindowSec) - _lastElapsedSec;
            if (overtimeRemaining <= 0)
            {
                shouldTriggerEnd = true;
                triggerReason = "Bomb overtime expired";
                _matchEnded = true;
            }
        }
    }

    private string? GetExpectedWinner()
    {
        if (_propState == PropState.Detonated)
        {
            return AttackingTeam;
        }

        if (_propState == PropState.Defused)
        {
            return DefendingTeam;
        }

        if (_plantTimeSec is not null)
        {
            var defuseWindowElapsed = _lastElapsedSec >= _plantTimeSec.Value + _matchOptions.DefuseWindowSec;
            if (defuseWindowElapsed)
            {
                return AttackingTeam;
            }
        }
        else if (_lastElapsedSec >= _matchOptions.AutoEndNoPlantAtSec)
        {
            return DefendingTeam;
        }

        return null;
    }

    private void PublishSnapshotLocked(string source)
    {
        var now = DateTimeOffset.UtcNow;
        var overtimeActive = _plantTimeSec is not null && _plantTimeSec.Value >= _matchOptions.AutoEndNoPlantAtSec && !_matchEnded;
        double? overtimeRemaining = null;
        if (overtimeActive)
        {
            overtimeRemaining = Math.Max(0, (_plantTimeSec!.Value + _matchOptions.DefuseWindowSec) - _lastElapsedSec);
        }

        var propTimerRemainingMs = GetPropTimerRemainingMs(now);

        var players = _lastSnapshotPayload?.Players ?? Array.Empty<MatchPlayerSnapshotDto>();
        var teamPlayerCounts = BuildTeamPlayerCounts(players);

        var snapshot = new MatchStateSnapshot(
            MatchId: _currentMatchId,
            LifecycleState: _lifecycleState,
            PropState: _propState,
            PlantTimeSec: _plantTimeSec,
            RemainingTimeMs: _lastMatchRemainingMs,
            IsOvertime: overtimeActive,
            OvertimeRemainingSec: overtimeRemaining,
            PropTimerRemainingMs: propTimerRemainingMs,
            PropTimerSyncedAt: _propTimerSyncedAt,
            LastPropUpdate: _lastPropUpdate,
            LastClockUpdate: _lastClockUpdate,
            PropLatency: _propLatency,
            ClockLatency: _clockLatency,
            LastActionDescription: _lastActionDescription,
            FocusAcquired: _focusAcquired,
            Players: players,
            TeamPlayerCounts: teamPlayerCounts);

        CurrentSnapshot = snapshot;
        SnapshotUpdated?.Invoke(this, snapshot);

        if (_relayService.IsEnabled)
        {
            var clockPayload = _lastSnapshotPayload;
            if (clockPayload is not null && IsTerminalStatus(clockPayload.Status))
            {
                var expectedWinner = GetExpectedWinner();
                var isWinnerMismatch = !string.IsNullOrWhiteSpace(expectedWinner)
                    && !string.Equals(clockPayload.WinnerTeam, expectedWinner, StringComparison.Ordinal);

                if (isWinnerMismatch)
                {
                    clockPayload = new MatchSnapshotDto
                    {
                        Id = clockPayload.Id,
                        Timestamp = clockPayload.Timestamp,
                        IsLastSend = clockPayload.IsLastSend,
                        Status = clockPayload.Status,
                        RemainingTimeMs = clockPayload.RemainingTimeMs,
                        WinnerTeam = expectedWinner!,
                        Players = clockPayload.Players
                    };
                }
            }

            if (_relayService.CanRelayMatch)
            {
                var matchRelayPayload = new
                {
                    match = _currentMatchId,
                    prop = _lastPropPayload,
                    clock = clockPayload,
                    fsm = snapshot
                };

                _ = _relayService.TryRelayMatchAsync(matchRelayPayload, CancellationToken.None);
            }

            if (_relayService.CanRelayProp && _lastPropPayload is not null)
            {
                var propRelayPayload = new
                {
                    match = _currentMatchId,
                    prop = _lastPropPayload,
                    fsm = snapshot
                };

                _ = _relayService.TryRelayPropAsync(propRelayPayload, CancellationToken.None);
            }
        }

        _logger.LogDebug("State updated via {Source}: {@Snapshot}", source, snapshot);
    }

    private double? GetPropTimerRemainingMs(DateTimeOffset now)
    {
        if (_propTimerRemainingMs is null || _propTimerSyncedAt is null)
        {
            return null;
        }

        if (_propState != PropState.Armed)
        {
            return _propTimerRemainingMs;
        }

        var elapsedMs = (now - _propTimerSyncedAt.Value).TotalMilliseconds;
        var remainingMs = _propTimerRemainingMs.Value - elapsedMs;
        return Math.Max(0, remainingMs);
    }

    private static IReadOnlyList<TeamPlayerCountSnapshot> BuildTeamPlayerCounts(IReadOnlyList<MatchPlayerSnapshotDto> players)
    {
        if (players.Count == 0)
        {
            return Array.Empty<TeamPlayerCountSnapshot>();
        }

        var grouped = players
            .GroupBy(player => string.IsNullOrWhiteSpace(player.Team) ? "Unknown" : player.Team)
            .Select(group => new TeamPlayerCountSnapshot(
                Team: group.Key,
                Alive: group.Count(IsPlayerAlive),
                Total: group.Count()))
            .OrderBy(result => result.Team, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return grouped;
    }

    private static bool IsPlayerAlive(MatchPlayerSnapshotDto player)
    {
        if (player.Health > 0)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(player.State))
        {
            return false;
        }

        return string.Equals(player.State, "alive", StringComparison.OrdinalIgnoreCase)
            || string.Equals(player.State, "active", StringComparison.OrdinalIgnoreCase);
    }

    private void ResetForNewMatch(string matchId)
    {
        _logger.LogInformation("Switching to match {MatchId}", matchId);
        _currentMatchId = matchId;
        _propState = PropState.Idle;
        _plantTimeSec = null;
        _matchEnded = false;
        _lastPropTimestamp = 0;
        _lastSnapshotTimestamp = 0;
        _lastElapsedSec = 0;
        _lastMatchRemainingMs = (int)(_matchOptions.LtDisplayedDurationSec * 1000);
        _lastActionDescription = "New match";
        _focusAcquired = false;
        _lastPropPayload = null;
        _lastSnapshotPayload = null;
        _propLatencyWindow.Clear();
        _clockLatencyWindow.Clear();
        _propLatencySamplesUntilPublish = 0;
        _clockLatencySamplesUntilPublish = 0;
        _propLatency = null;
        _clockLatency = null;
        _propTimerRemainingMs = null;
        _propTimerSyncedAt = null;
    }

    private static DateTimeOffset ParseSnapshotTimestamp(long timestamp, DateTimeOffset fallback)
    {
        try
        {
            // Prefer ticks when the payload uses DateTime ticks, otherwise interpret large numbers
            // as milliseconds and smaller values as Unix seconds.
            if (timestamp >= 1_000_000_000_000_000)
            {
                return new DateTimeOffset(new DateTime(timestamp, DateTimeKind.Utc));
            }

            if (timestamp >= 10_000_000_000)
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(timestamp);
            }

            return DateTimeOffset.FromUnixTimeSeconds(timestamp);
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    private async Task TriggerEndMatchAsync(string reason, CancellationToken cancellationToken)
    {
        bool shouldAutomate = false;
        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            if (now - _lastAutomationAt >= TimeSpan.FromMilliseconds(_uiAutomationOptions.DebounceWindowMs))
            {
                shouldAutomate = true;
                _lastAutomationAt = now;
                _lastActionDescription = $"Triggering end: {reason}";
            }
            else
            {
                _logger.LogInformation("End match trigger for {Reason} ignored due to debounce window", reason);
            }
        }

        if (!shouldAutomate)
        {
            return;
        }

        _logger.LogInformation("Issuing EndMatch due to {Reason}", reason);
        var result = await _focusService.TryEndMatchAsync(reason, cancellationToken).ConfigureAwait(false);
        ReportFocusResult(result.FocusAcquired, result.Description);
    }

    private static bool IsPlantState(PropState state)
    {
        return state is PropState.Arming or PropState.Armed;
    }

    private static bool IsTerminalPropState(PropState state)
    {
        return state is PropState.Defused or PropState.Detonated;
    }

    private static bool IsTerminalState(MatchLifecycleState state)
    {
        return state is MatchLifecycleState.WaitingOnFinalData or MatchLifecycleState.Completed or MatchLifecycleState.Cancelled;
    }

    private static bool IsTerminalStatus(MatchSnapshotStatus status)
    {
        return status is MatchSnapshotStatus.WaitingOnFinalData or MatchSnapshotStatus.Completed or MatchSnapshotStatus.Cancelled;
    }

    private LatencySampleSnapshot? UpdateLatencyMetrics(Queue<TimeSpan> samples, TimeSpan sample, ref int samplesUntilPublish)
    {
        samples.Enqueue(sample);

        var windowSize = Math.Max(1, _matchOptions.LatencyWindow);
        while (samples.Count > windowSize)
        {
            samples.Dequeue();
        }

        samplesUntilPublish++;
        if (samplesUntilPublish < windowSize)
        {
            return null;
        }

        samplesUntilPublish = 0;

        var min = samples.Min();
        var max = samples.Max();
        var averageMs = samples.Average(value => value.TotalMilliseconds);
        var average = TimeSpan.FromMilliseconds(averageMs);

        return new LatencySampleSnapshot(Average: average, Minimum: min, Maximum: max, SampleCount: samples.Count);
    }
}
