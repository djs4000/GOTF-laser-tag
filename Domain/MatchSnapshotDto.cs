using System.Text.Json.Serialization;

namespace LaserTag.Defusal.Domain;

/// <summary>
/// Incoming payload representing the overall match snapshot reported by the laser tag host.
/// </summary>
public sealed class MatchSnapshotDto
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

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

/// <summary>
/// Per-player snapshot information included in the match payload.
/// </summary>
public sealed class MatchPlayerSnapshotDto
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("team")]
    public required string Team { get; init; }

    [JsonPropertyName("state")]
    public required string State { get; init; }

    [JsonPropertyName("health")]
    public int Health { get; init; }

    [JsonPropertyName("headshots")]
    public int Headshots { get; init; }

    [JsonPropertyName("kills")]
    public IReadOnlyList<MatchPlayerKillDto> Kills { get; init; } = Array.Empty<MatchPlayerKillDto>();

    [JsonPropertyName("kills_count")]
    public int KillsCount { get; init; }

    [JsonPropertyName("deaths")]
    public int Deaths { get; init; }

    [JsonPropertyName("shots_hit")]
    public int ShotsHit { get; init; }

    [JsonPropertyName("shots_fired")]
    public int ShotsFired { get; init; }

    [JsonPropertyName("ammo")]
    public int Ammo { get; init; }
}

/// <summary>
/// Detailed information for each kill attributed to a player.
/// </summary>
public sealed class MatchPlayerKillDto
{
    [JsonPropertyName("username")]
    public required string Username { get; init; }

    [JsonPropertyName("secondsFromStart")]
    public required double SecondsFromStart { get; init; }

    [JsonPropertyName("location")]
    public required string Location { get; init; }
}
