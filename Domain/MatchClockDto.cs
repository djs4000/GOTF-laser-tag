using System.Text.Json.Serialization;

namespace LaserTag.Defusal.Domain;

/// <summary>
/// Payload describing the live match clock state.
/// </summary>
public sealed class MatchClockDto
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; init; }

    [JsonPropertyName("status")]
    public required MatchClockStatus Status { get; init; }

    [JsonPropertyName("remaining_time_ms")]
    public required int RemainingTimeMs { get; init; }

    [JsonPropertyName("winner_team")]
    public string? WinnerTeam { get; init; }
}
