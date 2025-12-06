using System.Text.Json.Serialization;

namespace LaserTag.Defusal.Domain;

/// <summary>
/// Combined payload containing the latest match and prop updates for downstream relays.
/// The schema mirrors specs/001-relay-winner-cleanup/contracts/combined-relay.json.
/// </summary>
public sealed class CombinedRelayPayload
{
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; init; }

    [JsonPropertyName("winner_team")]
    public string? WinnerTeam { get; init; }

    [JsonPropertyName("winner_reason")]
    public WinnerReason? WinnerReason { get; init; }

    [JsonPropertyName("match")]
    public required MatchSnapshotDto Match { get; init; }

    [JsonPropertyName("prop")]
    public required PropStatusDto Prop { get; init; }
}
