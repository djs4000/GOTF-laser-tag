using System.Text.Json.Serialization;

namespace LaserTag.Defusal.Domain;

/// <summary>
/// Response payload returned to the prop to reflect the current match status.
/// </summary>
public sealed class PropUpdateResponseDto
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("match_status")]
    public required string MatchStatus { get; init; }

    [JsonPropertyName("remaining_time_ms")]
    public required int RemainingTimeMs { get; init; }

    [JsonPropertyName("timestamp")]
    public required long Timestamp { get; init; }
}
