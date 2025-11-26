namespace LaserTag.Defusal.Domain;

/// <summary>
/// Represents the macro state of the laser tag match lifecycle.
/// </summary>
public enum MatchLifecycleState
{
    Idle,
    WaitingOnStart,
    Countdown,
    Running,
    WaitingOnFinalData,
    Completed,
    Cancelled
}
