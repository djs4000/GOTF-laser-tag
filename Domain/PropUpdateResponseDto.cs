using System.Text.Json.Serialization;

namespace LaserTag.Defusal.Domain;

/// <summary>
/// Response payload returned to the prop after processing a status update.
/// </summary>
public sealed class PropUpdateResponseDto
{
    [JsonPropertyName("status")]
    public required MatchSnapshotStatus Status { get; init; }

    [JsonPropertyName("remaining_time_ms")]
    public required int RemainingTimeMs { get; init; }

    [JsonPropertyName("timestamp")]
    public required long Timestamp { get; init; }
}
