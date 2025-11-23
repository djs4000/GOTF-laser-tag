using System.Text.Json.Serialization;

namespace LaserTag.Defusal.Domain;

/// <summary>
/// Response payload sent back to the prop acknowledging its latest state.
/// </summary>
public sealed class PropStatusResponseDto
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("remaining_time_ms")]
    public int RemainingTimeMs { get; init; }

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; init; }
}
