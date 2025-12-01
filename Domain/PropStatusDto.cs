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

    /// <summary>
    /// Remaining milliseconds reported by the prop when it is armed.
    /// </summary>
    [JsonPropertyName("timer")]
    public int? TimerMs { get; init; }

    /// <summary>
    /// Monotonic uptime in milliseconds reported by the prop, used for time synchronization.
    /// </summary>
    [JsonPropertyName("uptime_ms")]
    public long? UptimeMs { get; init; }
}
