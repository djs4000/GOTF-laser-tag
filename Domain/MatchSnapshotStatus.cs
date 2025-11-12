namespace LaserTag.Defusal.Domain;

/// <summary>
/// Enumerates the possible lifecycle states reported by the match snapshot payload.
/// </summary>
public enum MatchSnapshotStatus
{
    WaitingOnStart,
    Countdown,
    Running,
    WaitingOnFinalData,
    Completed,
    Cancelled
}
