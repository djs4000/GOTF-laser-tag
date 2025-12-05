namespace LaserTag.Defusal.Domain;

/// <summary>
/// Combined payload containing the latest match and prop updates for downstream relays.
/// </summary>
public sealed class CombinedRelayPayload
{
    public MatchRelayDto? Match { get; init; }

    public PropStatusDto? Prop { get; init; }
}
