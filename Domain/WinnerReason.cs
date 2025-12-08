namespace LaserTag.Defusal.Domain;

/// <summary>
/// Identifies which authority resolved the winner for the relay payload.
/// </summary>
public enum WinnerReason
{
    TeamElimination,
    ObjectiveDetonated,
    ObjectiveDefused,
    TimeExpiration
}
