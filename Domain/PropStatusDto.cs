using System.Text.Json.Serialization;

namespace LaserTag.Defusal.Domain;

/// <summary>
/// Incoming payload from the prop controller describing the bomb state.
/// </summary>
public sealed class PropStatusDto
{
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; init; }

    [JsonPropertyName("state")]
    public required PropState State { get; init; }
}
