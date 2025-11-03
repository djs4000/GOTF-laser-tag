namespace LaserTag.Defusal.Domain;

/// <summary>
/// Represents the set of possible states reported by the physical prop.
/// </summary>
public enum PropState
{
    Idle,
    Armed,
    Planted,
    Defusing,
    Defused,
    Exploded
}
