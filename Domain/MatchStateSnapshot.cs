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
    DateTimeOffset? LastPropUpdate,
    DateTimeOffset? LastClockUpdate,
    TimeSpan? LastClockLatency,
    string LastActionDescription,
    bool FocusAcquired)
{
    public static readonly MatchStateSnapshot Default = new(
        MatchId: null,
        LifecycleState: MatchLifecycleState.Idle,
        PropState: PropState.Idle,
        PlantTimeSec: null,
        RemainingTimeMs: null,
        IsOvertime: false,
        OvertimeRemainingSec: null,
        LastPropUpdate: null,
        LastClockUpdate: null,
        LastClockLatency: null,
        LastActionDescription: "Idle (no match data)",
        FocusAcquired: false);
}
