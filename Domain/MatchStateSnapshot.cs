namespace LaserTag.Defusal.Domain;

/// <summary>
/// Immutable projection of the match coordinator state used for UI updates and relays.
/// </summary>
public sealed record MatchStateSnapshot(
    string? MatchId,
    MatchLifecycleState LifecycleState,
    PropState PropState,
    string? PropReplyStatus,
    double? PlantTimeSec,
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
        LifecycleState: MatchLifecycleState.Freezetime,
        PropState: PropState.Idle,
        PropReplyStatus: null,
        PlantTimeSec: null,
        IsOvertime: false,
        OvertimeRemainingSec: null,
        LastPropUpdate: null,
        LastClockUpdate: null,
        LastClockLatency: null,
        LastActionDescription: "Idle",
        FocusAcquired: false);
}
