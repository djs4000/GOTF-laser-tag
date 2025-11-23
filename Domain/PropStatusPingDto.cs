using System.Text.Json.Serialization;

namespace LaserTag.Defusal.Domain;

/// <summary>
/// Payload sent by the prop requesting the current match state.
/// </summary>
public sealed class PropStatusPingDto
{
    [JsonPropertyName("state")]
    public string? State { get; init; }

    [JsonPropertyName("timer")]
    public int? Timer { get; init; }

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; init; }
}

/// <summary>
/// Reply payload sent back to the prop describing the current match status.
/// </summary>
public sealed class PropStatusResponseDto
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("remaining_time_ms")]
    public required int RemainingTimeMs { get; init; }

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; init; }
}
