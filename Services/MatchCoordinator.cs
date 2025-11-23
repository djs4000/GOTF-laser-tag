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
    private readonly object _sync = new();

    private MatchLifecycleState _lifecycleState = MatchLifecycleState.Idle;
    private PropState _propState = PropState.Idle;
    private double? _plantTimeSec;
    private bool _matchEnded;
    private string? _currentMatchId;
    private long _lastPropTimestamp;
    private long _lastSnapshotTimestamp;
    private double _lastElapsedSec;
    private DateTimeOffset? _lastClockUpdate;
    private DateTimeOffset? _lastPropUpdate;
    private TimeSpan? _lastClockLatency;
    private string _lastActionDescription = "Idle";
    private bool _focusAcquired;
    private DateTimeOffset _lastAutomationAt = DateTimeOffset.MinValue;
    private PropStatusDto? _lastPropPayload;
    private MatchSnapshotDto? _lastSnapshotPayload;

    public event EventHandler<MatchStateSnapshot>? SnapshotUpdated;

    public MatchStateSnapshot CurrentSnapshot { get; private set; } = MatchStateSnapshot.Default;

    public MatchCoordinator(
        IOptions<MatchOptions> matchOptions,
        RelayService relayService,
        IFocusService focusService,
        IOptions<UiAutomationOptions> uiAutomationOptions,
        ILogger<MatchCoordinator> logger)
    {
        _relayService = relayService;
        _focusService = focusService;
        _uiAutomationOptions = uiAutomationOptions.Value;
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

        lock (_sync)
        {
            if (_matchEnded && IsTerminalState(_lifecycleState))
            {
                return CurrentSnapshot;
            }

            if (_lifecycleState is MatchLifecycleState.Idle or MatchLifecycleState.WaitingOnStart or MatchLifecycleState.Countdown)
            {
                _logger.LogInformation("Ignoring prop update while match inactive: {State}", dto.State);
                return CurrentSnapshot;
            }

            if (dto.Timestamp < _lastPropTimestamp)
            {
                _logger.LogWarning("Out-of-order prop payload ignored (timestamp {Timestamp})", dto.Timestamp);
                return CurrentSnapshot;
            }

            incomingState = dto.State;
            _lastPropTimestamp = dto.Timestamp;
            _lastPropUpdate = DateTimeOffset.UtcNow;
            _lastPropPayload = dto;

            if (incomingState == PropState.Planted)
            {
                if (_plantTimeSec is null || Math.Abs(_plantTimeSec.Value - _lastElapsedSec) > double.Epsilon)
                {
                    _plantTimeSec = _lastElapsedSec;
                    _logger.LogInformation("Bomb planted at {Elapsed:F1}s", _plantTimeSec);
                }
            }

            if (incomingState is PropState.Defused or PropState.Exploded)
            {
                shouldTriggerEnd = true;
                triggerReason = incomingState == PropState.Defused ? "Prop defused" : "Prop exploded";
                _matchEnded = true;
                _lifecycleState = MatchLifecycleState.WaitingOnFinalData;
            }

            _propState = incomingState;
            PublishSnapshotLocked("Prop update");
        }

        if (shouldTriggerEnd)
        {
            await TriggerEndMatchAsync(triggerReason!, cancellationToken).ConfigureAwait(false);
        }

        return CurrentSnapshot;
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
            DateTimeOffset sourceTimestamp;
            try
            {
                sourceTimestamp = new DateTimeOffset(new DateTime(dto.Timestamp, DateTimeKind.Utc));
            }
            catch (ArgumentOutOfRangeException)
            {
                _logger.LogWarning("Match snapshot timestamp {Timestamp} is out of range", dto.Timestamp);
                sourceTimestamp = now;
            }
            _lastClockLatency = now - sourceTimestamp;

            if (!string.Equals(_currentMatchId, dto.Id, StringComparison.Ordinal))
            {
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
                    _propState = PropState.Idle;
                    break;
                case MatchSnapshotStatus.Countdown:
                    _matchEnded = false;
                    _lifecycleState = MatchLifecycleState.Countdown;
                    _plantTimeSec = null;
                    _propState = PropState.Idle;
                    break;
                case MatchSnapshotStatus.Running:
                    _matchEnded = false;
                    if (_propState == PropState.Idle)
                    {
                        _propState = PropState.Armed;
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
            _propState = PropState.Armed;
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
            _lastClockUpdate = null;
            _lastPropUpdate = null;
            _lastClockLatency = null;
            _lastActionDescription = "Idle (no match data)";
            _focusAcquired = false;
            _lastPropPayload = null;
            _lastSnapshotPayload = null;
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

        if (_propState is PropState.Defused or PropState.Exploded)
        {
            shouldTriggerEnd = true;
            triggerReason = _propState == PropState.Defused ? "Prop defused" : "Prop exploded";
            _matchEnded = true;
            _lifecycleState = MatchLifecycleState.WaitingOnFinalData;
            return;
        }

        if (_plantTimeSec is null && _lastElapsedSec >= _matchOptions.AutoEndNoPlantAtSec)
        {
            shouldTriggerEnd = true;
            triggerReason = $"No plant by {_matchOptions.AutoEndNoPlantAtSec}s";
            _matchEnded = true;
            _lifecycleState = MatchLifecycleState.WaitingOnFinalData;
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
                _lifecycleState = MatchLifecycleState.WaitingOnFinalData;
            }
        }
    }

    private void PublishSnapshotLocked(string source)
    {
        var overtimeActive = _plantTimeSec is not null && _plantTimeSec.Value >= _matchOptions.AutoEndNoPlantAtSec && !_matchEnded;
        double? overtimeRemaining = null;
        if (overtimeActive)
        {
            overtimeRemaining = Math.Max(0, (_plantTimeSec!.Value + _matchOptions.DefuseWindowSec) - _lastElapsedSec);
        }

        var snapshot = new MatchStateSnapshot(
            MatchId: _currentMatchId,
            LifecycleState: _lifecycleState,
            PropState: _propState,
            PlantTimeSec: _plantTimeSec,
            IsOvertime: overtimeActive,
            OvertimeRemainingSec: overtimeRemaining,
            LastPropUpdate: _lastPropUpdate,
            LastClockUpdate: _lastClockUpdate,
            LastClockLatency: _lastClockLatency,
            LastActionDescription: _lastActionDescription,
            FocusAcquired: _focusAcquired);

        CurrentSnapshot = snapshot;
        SnapshotUpdated?.Invoke(this, snapshot);

        if (_relayService.IsEnabled)
        {
            var relayPayload = new
            {
                match = _currentMatchId,
                prop = _lastPropPayload,
                clock = _lastSnapshotPayload,
                fsm = snapshot
            };
            _ = _relayService.TryRelayAsync(relayPayload, CancellationToken.None);
        }

        _logger.LogDebug("State updated via {Source}: {@Snapshot}", source, snapshot);
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
        _lastActionDescription = "New match";
        _focusAcquired = false;
        _lastPropPayload = null;
        _lastSnapshotPayload = null;
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

    private static bool IsTerminalState(MatchLifecycleState state)
    {
        return state is MatchLifecycleState.WaitingOnFinalData or MatchLifecycleState.Completed or MatchLifecycleState.Cancelled;
    }

    private static bool IsTerminalStatus(MatchSnapshotStatus status)
    {
        return status is MatchSnapshotStatus.WaitingOnFinalData or MatchSnapshotStatus.Completed or MatchSnapshotStatus.Cancelled;
    }
}
