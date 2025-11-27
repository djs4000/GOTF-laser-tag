using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LaserTag.Defusal.Domain;

/// <summary>
/// Optional verbose diagnostics returned to the prop when debug mode is enabled.
/// </summary>
public sealed class PropDiagnosticsDto
{
    [JsonPropertyName("match_id")]
    public string? MatchId { get; init; }

    [JsonPropertyName("lifecycle_state")]
    public string LifecycleState { get; init; } = string.Empty;

    [JsonPropertyName("prop_state")]
    public string PropState { get; init; } = string.Empty;

    [JsonPropertyName("match_ended")]
    public bool MatchEnded { get; init; }

    [JsonPropertyName("focus_acquired")]
    public bool FocusAcquired { get; init; }

    [JsonPropertyName("last_action")]
    public string? LastAction { get; init; }

    [JsonPropertyName("remaining_time_ms")]
    public int? LastKnownRemainingMs { get; init; }

    [JsonPropertyName("clock_latency_ms")]
    public int? LastClockLatencyMs { get; init; }

    [JsonPropertyName("prop_age_ms")]
    public int? LastPropAgeMs { get; init; }

    [JsonPropertyName("error_causes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? ErrorCauses { get; init; }
}
