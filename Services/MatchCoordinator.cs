using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LaserTag.Defusal.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LaserTag.Defusal.Services;

/// <summary>
/// Central orchestrator that consumes prop and clock updates, evaluates match-ending conditions,
/// and issues UI automation commands when required.
/// </summary>
public sealed class MatchCoordinator : IDisposable
{
    private readonly ILogger<MatchCoordinator> _logger;
    private readonly MatchOptions _matchOptions;
    private readonly IRelayService _relayService;
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
    private double? _autoEndAnchorElapsedSec;
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
    private string? _winnerTeam;
    private WinnerReason? _winnerReason;
    private double? _propTimerRemainingMs;
    private DateTimeOffset? _propTimerSyncedAt;
    private string _attackingTeam = "Team 1";
    private CancellationTokenSource? _finalDataTimeoutCts;
    private bool _relayFinalizedForCurrentMatch;

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
        IRelayService relayService,
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
                PublishSnapshotLocked("Prop update (inactive match)", eventTimestamp: normalizedTimestampMs);
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
                var winnerTeam = incomingState == PropState.Defused ? DefendingTeam : AttackingTeam;
                var reason = incomingState == PropState.Defused ? WinnerReason.ObjectiveDefused : WinnerReason.ObjectiveDetonated;
                SetWinnerLocked(winnerTeam, reason, triggerReason);
                MarkMatchEndedLocked(triggerReason);
            }

            PublishSnapshotLocked("Prop update", eventTimestamp: normalizedTimestampMs);
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
        CombinedRelayPayload? relayPayload = null;
        bool shouldRelayPayload = false;

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
                var preservePropState = _lastPropPayload is not null;
                ResetForNewMatch(dto.Id, preservePropState);
            }
            else if (isNewMatchId)
            {
                _relayFinalizedForCurrentMatch = false;
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

            if (_matchEnded && IsTerminalState(_lifecycleState) && dto.Status == MatchSnapshotStatus.Running)
            {
                _logger.LogDebug("Ignoring running snapshot while match is ended ({LifecycleState})", _lifecycleState);
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

            if (dto.IsLastSend || dto.Status is MatchSnapshotStatus.Completed or MatchSnapshotStatus.WaitingOnFinalData)
            {
                TryResolveEliminationWinnerLocked(dto.Players);
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
                    if (_autoEndAnchorElapsedSec is null)
                    {
                        _autoEndAnchorElapsedSec = _lastElapsedSec;
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
                    if (dto.IsLastSend)
                    {
                        CancelFinalDataTimeoutLocked();
                    }
                    else
                    {
                        EnsureFinalDataTimeoutScheduledLocked();
                    }
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

            var relayOverrides = BuildRelayOverridesLocked(dto);
            relayPayload = BuildCombinedPayloadLocked(
                forceRelay: relayOverrides.IsLastSendOverride ?? false,
                eventTimestamp: dto.Timestamp,
                overrides: relayOverrides);

            shouldRelayPayload = IsRelayActiveStatus(dto.Status) && !_relayFinalizedForCurrentMatch;
            if (shouldRelayPayload && relayOverrides.FinalizeRelay)
            {
                _relayFinalizedForCurrentMatch = true;
            }

            PublishSnapshotLocked("Match update", relayPayload, eventTimestamp: dto.Timestamp);
        }

        if (shouldRelayPayload && relayPayload is not null && _relayService.IsEnabled)
        {
            _ = _relayService.TryRelayCombinedAsync(relayPayload, CancellationToken.None);
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
            _autoEndAnchorElapsedSec = 0;
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
            CancelFinalDataTimeoutLocked();
            _currentMatchId = null;
            _propState = PropState.Idle;
            _plantTimeSec = null;
            _matchEnded = false;
            _lifecycleState = MatchLifecycleState.Idle;
            _lastPropTimestamp = 0;
            _lastSnapshotTimestamp = 0;
            _lastElapsedSec = 0;
            _autoEndAnchorElapsedSec = null;
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
            _winnerTeam = null;
            _winnerReason = null;
            _relayFinalizedForCurrentMatch = false;
            PublishSnapshotLocked("Idle state");
        }
    }

    /// <summary>
    /// Allows the operator to manually trigger the end-match automation.
    /// </summary>
    public Task ForceEndMatchAsync(string reason, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            MarkMatchEndedLocked(reason);
            PublishSnapshotLocked("Manual end");
        }

        return TriggerEndMatchAsync(reason, cancellationToken);
    }

    private void EvaluateLiveState(ref bool shouldTriggerEnd, ref string? triggerReason)
    {
        if (_matchEnded)
        {
            return;
        }

        var observedElapsedSinceTrackingStart = _autoEndAnchorElapsedSec is null
            ? _lastElapsedSec
            : Math.Max(0, _lastElapsedSec - _autoEndAnchorElapsedSec.Value);

        if (IsTerminalPropState(_propState))
        {
            shouldTriggerEnd = true;
            triggerReason = _propState == PropState.Defused ? "Prop defused" : "Prop detonated";
            MarkMatchEndedLocked(triggerReason);
            return;
        }

        if (_plantTimeSec is null && observedElapsedSinceTrackingStart >= _matchOptions.AutoEndNoPlantAtSec)
        {
            shouldTriggerEnd = true;
            triggerReason = $"No plant by {_matchOptions.AutoEndNoPlantAtSec}s";
            SetWinnerLocked(DefendingTeam, WinnerReason.TimeExpiration, triggerReason);
            MarkMatchEndedLocked(triggerReason);
            return;
        }

        if (_plantTimeSec is not null && _plantTimeSec.Value >= _matchOptions.AutoEndNoPlantAtSec)
        {
            var overtimeRemaining = (_plantTimeSec.Value + _matchOptions.DefuseWindowSec) - _lastElapsedSec;
            if (overtimeRemaining <= 0)
            {
                shouldTriggerEnd = true;
                triggerReason = "Bomb overtime expired";
                SetWinnerLocked(AttackingTeam, WinnerReason.ObjectiveDetonated, triggerReason);
                MarkMatchEndedLocked(triggerReason);
            }
        }
    }

    private void MarkMatchEndedLocked(string reason)
    {
        _matchEnded = true;

        if (!IsTerminalState(_lifecycleState))
        {
            _lifecycleState = MatchLifecycleState.WaitingOnFinalData;
        }

        if (_lastMatchRemainingMs is null)
        {
            _lastMatchRemainingMs = 0;
        }

            _lastActionDescription = $"Ended: {reason}";
    }

    private void TryResolveEliminationWinnerLocked(IReadOnlyList<MatchPlayerSnapshotDto> players)
    {
        if (_winnerReason is not null)
        {
            return;
        }

        if (players is null || players.Count == 0)
        {
            return;
        }

        var teamCounts = BuildTeamPlayerCounts(players);
        var aliveTeams = teamCounts
            .Where(team => team.Alive > 0 && !string.Equals(team.Team, "Unknown", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (aliveTeams.Length != 1)
        {
            return;
        }

        var winnerTeam = aliveTeams[0].Team;
        var opposingTeamPresent = teamCounts.Any(team =>
            !string.Equals(team.Team, winnerTeam, StringComparison.OrdinalIgnoreCase)
            && team.Total > 0);

        if (!opposingTeamPresent)
        {
            return;
        }

        SetWinnerLocked(winnerTeam, WinnerReason.TeamElimination, "All opposing players eliminated");
    }

    private RelayBuildOverrides BuildRelayOverridesLocked(MatchSnapshotDto snapshot)
    {
        var now = DateTimeOffset.UtcNow;
        var winnerTeam = _winnerTeam;
        var winnerReason = _winnerReason;
        var winDetected = winnerTeam is not null && winnerReason is not null;

        if (_propState == PropState.Detonated)
        {
            winnerTeam ??= AttackingTeam;
            winnerReason ??= WinnerReason.ObjectiveDetonated;
            winDetected = true;
        }
        else if (_propState == PropState.Defused)
        {
            winnerTeam ??= DefendingTeam;
            winnerReason ??= WinnerReason.ObjectiveDefused;
            winDetected = true;
        }
        else
        {
            var propTimerRemainingMs = GetPropTimerRemainingMs(now);
            if (propTimerRemainingMs is not null && propTimerRemainingMs <= 0)
            {
                winnerTeam ??= AttackingTeam;
                winnerReason ??= WinnerReason.ObjectiveDetonated;
                winDetected = true;
            }

            if (!winDetected)
            {
                int? remainingForWinCheck = snapshot.RemainingTimeMs;
                if (remainingForWinCheck is null && _lastMatchRemainingMs is not null)
                {
                    remainingForWinCheck = _lastMatchRemainingMs;
                }

                if (IsMatchTimerExpired(remainingForWinCheck))
                {
                    winnerTeam ??= DefendingTeam;
                    winnerReason ??= WinnerReason.TimeExpiration;
                    winDetected = true;
                }
            }

            if (!winDetected && TryDetectEliminationWinner(snapshot.Players ?? Array.Empty<MatchPlayerSnapshotDto>(), out var eliminationWinner))
            {
                winnerTeam ??= eliminationWinner;
                winnerReason ??= WinnerReason.TeamElimination;
                winDetected = true;
            }
        }

        if (winDetected && winnerTeam is not null && winnerReason is not null)
        {
            SetWinnerLocked(winnerTeam, winnerReason.Value, "Relay override");
            return new RelayBuildOverrides(
                StatusOverride: MatchSnapshotStatus.Completed,
                IsLastSendOverride: true,
                WinnerTeamOverride: winnerTeam,
                WinnerReasonOverride: winnerReason,
                FinalizeRelay: true);
        }

        return new RelayBuildOverrides(
            StatusOverride: null,
            IsLastSendOverride: null,
            WinnerTeamOverride: null,
            WinnerReasonOverride: winnerReason,
            FinalizeRelay: false);
    }

    private sealed record RelayBuildOverrides(
        MatchSnapshotStatus? StatusOverride,
        bool? IsLastSendOverride,
        string? WinnerTeamOverride,
        WinnerReason? WinnerReasonOverride,
        bool FinalizeRelay);

    private bool TryDetectEliminationWinner(IReadOnlyList<MatchPlayerSnapshotDto> players, out string winnerTeam)
    {
        winnerTeam = string.Empty;
        if (players is null || players.Count == 0)
        {
            return false;
        }

        var teamCounts = BuildTeamPlayerCounts(players);
        var aliveTeams = teamCounts
            .Where(team => team.Alive > 0 && !string.Equals(team.Team, "Unknown", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (aliveTeams.Length != 1)
        {
            return false;
        }

        winnerTeam = aliveTeams[0].Team;
        var resolvedWinnerTeam = winnerTeam;
        var opposingTeamPresent = teamCounts.Any(team =>
            !string.Equals(team.Team, resolvedWinnerTeam, StringComparison.OrdinalIgnoreCase)
            && team.Total > 0);

        return opposingTeamPresent;
    }

    /// <summary>
    /// Produces a combined payload using the latest buffered match and prop snapshots so every relay carries both components per AGENTS.md cadence rules.
    /// </summary>
    private CombinedRelayPayload BuildCombinedPayloadLocked(bool forceRelay, long? eventTimestamp, RelayBuildOverrides? overrides = null)
    {
        var matchPayload = BuildRelayMatchSnapshotLocked(forceRelay, overrides);
        var propPayload = BuildRelayPropSnapshotLocked(matchPayload.Timestamp);
        var timestamp = ResolveRelayTimestamp(eventTimestamp, matchPayload.Timestamp, propPayload.Timestamp);

        return new CombinedRelayPayload
        {
            Timestamp = timestamp,
            AttackingTeam = AttackingTeam,
            WinnerReason = overrides?.WinnerReasonOverride ?? _winnerReason,
            Match = matchPayload,
            Prop = propPayload
        };
    }

    private MatchSnapshotDto BuildRelayMatchSnapshotLocked(bool forceRelay, RelayBuildOverrides? overrides)
    {
        var statusOverride = overrides?.StatusOverride;
        var isLastSendOverride = overrides?.IsLastSendOverride;
        var winnerOverride = overrides?.WinnerTeamOverride;

        if (_lastSnapshotPayload is { } snapshot)
        {
            var timestamp = snapshot.Timestamp != 0
                ? snapshot.Timestamp
                : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var status = statusOverride ?? snapshot.Status;
            var isLastSend = isLastSendOverride
                ?? snapshot.IsLastSend
                || (forceRelay && IsTerminalStatus(status));

            return new MatchSnapshotDto
            {
                Id = snapshot.Id,
                Timestamp = timestamp,
                IsLastSend = isLastSend,
                Status = status,
                RemainingTimeMs = snapshot.RemainingTimeMs,
                WinnerTeam = winnerOverride ?? _winnerTeam ?? snapshot.WinnerTeam,
                Players = snapshot.Players ?? Array.Empty<MatchPlayerSnapshotDto>()
            };
        }

        var fallbackId = _currentMatchId ?? "pending";
        var fallbackTimestamp = _lastSnapshotTimestamp != 0
            ? _lastSnapshotTimestamp
            : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var fallbackRemaining = _lastMatchRemainingMs ?? (int)(_matchOptions.LtDisplayedDurationSec * 1000);

        var fallbackStatus = statusOverride ?? MapLifecycleToSnapshotStatus(_lifecycleState);
        return new MatchSnapshotDto
        {
            Id = fallbackId,
            Timestamp = fallbackTimestamp,
            IsLastSend = isLastSendOverride ?? (forceRelay && _matchEnded),
            Status = fallbackStatus,
            RemainingTimeMs = fallbackRemaining,
            WinnerTeam = winnerOverride ?? _winnerTeam,
            Players = Array.Empty<MatchPlayerSnapshotDto>()
        };
    }

    private PropStatusDto BuildRelayPropSnapshotLocked(long fallbackTimestamp)
    {
        if (_lastPropPayload is { } prop)
        {
            var timestamp = prop.Timestamp != 0 ? prop.Timestamp : fallbackTimestamp;
            return new PropStatusDto
            {
                Timestamp = timestamp,
                State = prop.State,
                TimerMs = prop.TimerMs,
                UptimeMs = prop.UptimeMs ?? 0
            };
        }

        var timestampFallback = _lastPropTimestamp != 0
            ? _lastPropTimestamp
            : fallbackTimestamp != 0
                ? fallbackTimestamp
                : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        int? timer = null;
        if (_propTimerRemainingMs is not null)
        {
            timer = (int)Math.Max(0, Math.Round(_propTimerRemainingMs.Value));
        }

        return new PropStatusDto
        {
            Timestamp = timestampFallback,
            State = _propState,
            TimerMs = timer,
            UptimeMs = 0
        };
    }

    private static long ResolveRelayTimestamp(long? candidate, long matchTimestamp, long propTimestamp)
    {
        if (candidate.HasValue && candidate.Value != 0)
        {
            return candidate.Value;
        }

        if (matchTimestamp != 0)
        {
            return matchTimestamp;
        }

        if (propTimestamp != 0)
        {
            return propTimestamp;
        }

        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    private static MatchSnapshotStatus MapLifecycleToSnapshotStatus(MatchLifecycleState state)
    {
        return state switch
        {
            MatchLifecycleState.Countdown => MatchSnapshotStatus.Countdown,
            MatchLifecycleState.Running => MatchSnapshotStatus.Running,
            MatchLifecycleState.WaitingOnFinalData => MatchSnapshotStatus.WaitingOnFinalData,
            MatchLifecycleState.Completed => MatchSnapshotStatus.Completed,
            MatchLifecycleState.Cancelled => MatchSnapshotStatus.Cancelled,
            _ => MatchSnapshotStatus.WaitingOnStart
        };
    }

    private void SetWinnerLocked(string winnerTeam, WinnerReason reason, string context)
    {
        if (_winnerReason is not null)
        {
            _logger.LogDebug(
                "Winner already resolved to {Winner} via {Reason}; ignoring {Context}",
                _winnerTeam ?? "<unknown>",
                _winnerReason,
                context);
            return;
        }

        _winnerTeam = winnerTeam;
        _winnerReason = reason;
        var contextLabel = string.IsNullOrWhiteSpace(context) ? "unspecified" : context;
        _logger.LogInformation("Winner resolved to {Winner} via {Reason} ({Context})", winnerTeam, reason, contextLabel);
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

    private static bool IsMatchTimerExpired(int? remainingTimeMs)
    {
        return remainingTimeMs is not null && remainingTimeMs <= 0;
    }

    private static bool IsRelayActiveStatus(MatchSnapshotStatus status)
    {
        return status is MatchSnapshotStatus.WaitingOnStart or MatchSnapshotStatus.Countdown or MatchSnapshotStatus.Running;
    }

    private void PublishSnapshotLocked(string source, CombinedRelayPayload? combinedRelayPayload = null, long? eventTimestamp = null)
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

        combinedRelayPayload ??= BuildCombinedPayloadLocked(forceRelay: false, eventTimestamp: eventTimestamp);

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
            TeamPlayerCounts: teamPlayerCounts,
            WinnerTeam: _winnerTeam ?? combinedRelayPayload.Match.WinnerTeam,
            WinnerReason: combinedRelayPayload.WinnerReason ?? _winnerReason,
            LatestCombinedRelayPayload: combinedRelayPayload);

        CurrentSnapshot = snapshot;
        SnapshotUpdated?.Invoke(this, snapshot);

        _logger.LogDebug(
            "State updated via {Source}: winner={Winner} reason={Reason}",
            source,
            snapshot.WinnerTeam ?? "<pending>",
            snapshot.WinnerReason?.ToString() ?? "<none>");
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

    private void ResetForNewMatch(string matchId, bool preservePropState = false)
    {
        _logger.LogInformation("Switching to match {MatchId}", matchId);
        _currentMatchId = matchId;
        if (!preservePropState)
        {
            _propState = PropState.Idle;
            _plantTimeSec = null;
            _lastPropTimestamp = 0;
            _lastPropPayload = null;
            _propTimerRemainingMs = null;
            _propTimerSyncedAt = null;
        }

        _matchEnded = false;
        _lastSnapshotTimestamp = 0;
        _lastElapsedSec = 0;
        _autoEndAnchorElapsedSec = null;
        _lastMatchRemainingMs = (int)(_matchOptions.LtDisplayedDurationSec * 1000);
        _lastActionDescription = preservePropState ? "New match (prop buffered)" : "New match";
        _focusAcquired = false;
        _lastSnapshotPayload = null;
        _propLatencyWindow.Clear();
        _clockLatencyWindow.Clear();
        _propLatencySamplesUntilPublish = 0;
        _clockLatencySamplesUntilPublish = 0;
        _propLatency = null;
        _clockLatency = null;
        _winnerTeam = null;
        _winnerReason = null;
        CancelFinalDataTimeoutLocked();
        _relayFinalizedForCurrentMatch = false;
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

    public void Dispose()
    {
        lock (_sync)
        {
            CancelFinalDataTimeoutLocked();
        }
    }

    private void EnsureFinalDataTimeoutScheduledLocked()
    {
        if (_finalDataTimeoutCts is not null)
        {
            return;
        }

        var timeoutMs = Math.Max(0, _matchOptions.FinalDataTimeoutMs);
        var cts = new CancellationTokenSource();
        _finalDataTimeoutCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(timeoutMs, cts.Token).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            lock (_sync)
            {
                if (cts.IsCancellationRequested)
                {
                    return;
                }

                _logger.LogWarning("Final data timeout - forcing relay");
                PublishSnapshotLocked("Final Data Timeout");
                CancelFinalDataTimeoutLocked();
            }
        });
    }

    private void CancelFinalDataTimeoutLocked()
    {
        _finalDataTimeoutCts?.Cancel();
        _finalDataTimeoutCts?.Dispose();
        _finalDataTimeoutCts = null;
    }
}
