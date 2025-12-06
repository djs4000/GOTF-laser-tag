using System;
using System.Text.Json.Serialization;

namespace LaserTag.Defusal.Domain;

/// <summary>
/// Outbound relay payload for match snapshots. Mirrors the incoming snapshot shape but
/// emits the match identifier under the <c>match</c> property for consistency across
/// raw and combined relays.
/// </summary>
public sealed class MatchRelayDto
{
    [JsonPropertyName("match")]
    public required string Match { get; init; }

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; init; }

    [JsonPropertyName("is_last_send")]
    public bool IsLastSend { get; init; }

    [JsonPropertyName("status")]
    public required MatchSnapshotStatus Status { get; init; }

    [JsonPropertyName("remaining_time_ms")]
    public required int RemainingTimeMs { get; init; }

    [JsonPropertyName("winner_team")]
    public string? WinnerTeam { get; init; }

    [JsonPropertyName("players")]
    public IReadOnlyList<MatchPlayerSnapshotDto> Players { get; init; } = Array.Empty<MatchPlayerSnapshotDto>();
}
