namespace LaserTag.Defusal.Domain;

/// <summary>
/// Enumerates the states reported by the match clock endpoint.
/// </summary>
public enum MatchClockStatus
{
    Freezetime,
    Live,
    Gameover
}
