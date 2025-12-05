using System;
using System.Collections.Generic;

namespace LaserTag.Defusal.Domain;

/// <summary>
/// Immutable projection of the match coordinator state used for UI updates and relays.
/// </summary>
public sealed record MatchStateSnapshot(
    string? MatchId,
    MatchLifecycleState LifecycleState,
    PropState PropState,
    double? PlantTimeSec,
    int? RemainingTimeMs,
    bool IsOvertime,
    double? OvertimeRemainingSec,
    double? PropTimerRemainingMs,
    DateTimeOffset? PropTimerSyncedAt,
    DateTimeOffset? LastPropUpdate,
    DateTimeOffset? LastClockUpdate,
    LatencySampleSnapshot? PropLatency,
    LatencySampleSnapshot? ClockLatency,
    string LastActionDescription,
    bool FocusAcquired,
    IReadOnlyList<MatchPlayerSnapshotDto> Players,
    IReadOnlyList<TeamPlayerCountSnapshot> TeamPlayerCounts,
    MatchRelayDto? LatestRelayPayload,
    CombinedRelayPayload? LatestCombinedRelayPayload)
{
    public static readonly MatchStateSnapshot Default = new(
        MatchId: null,
        LifecycleState: MatchLifecycleState.Idle,
        PropState: PropState.Idle,
        PlantTimeSec: null,
        RemainingTimeMs: null,
        IsOvertime: false,
        OvertimeRemainingSec: null,
        PropTimerRemainingMs: null,
        PropTimerSyncedAt: null,
        LastPropUpdate: null,
        LastClockUpdate: null,
        PropLatency: null,
        ClockLatency: null,
        LastActionDescription: "Idle (no match data)",
        FocusAcquired: false,
        Players: Array.Empty<MatchPlayerSnapshotDto>(),
        TeamPlayerCounts: Array.Empty<TeamPlayerCountSnapshot>(),
        LatestRelayPayload: null,
        LatestCombinedRelayPayload: null);
}

public sealed record TeamPlayerCountSnapshot(string Team, int Alive, int Total)
{
    public override string ToString() => $"{Team}: {Alive}/{Total} alive";
}
