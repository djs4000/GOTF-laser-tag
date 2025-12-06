using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LaserTag.Defusal.Domain;

/// <summary>
/// Combined payload containing the latest match and prop updates for downstream relays.
/// The schema mirrors specs/001-relay-winner-cleanup/contracts/combined-relay.json.
/// </summary>
public sealed class CombinedRelayPayload
{
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; init; }

    [JsonPropertyName("winner_team")]
    public string? WinnerTeam { get; init; }

    [JsonPropertyName("winner_reason")]
    public WinnerReason? WinnerReason { get; init; }

    [JsonPropertyName("match")]
    public required MatchSnapshotDto Match { get; init; }

    [JsonPropertyName("prop")]
    public required PropStatusDto Prop { get; init; }

    /// <summary>
    /// Validates the payload against the contract guarantees defined in specs/001-relay-winner-cleanup/contracts/combined-relay.json.
    /// </summary>
    public bool TryValidate(out IReadOnlyList<string> errors)
    {
        var issues = new List<string>();

        if (Match is null)
        {
            issues.Add("Missing match object.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(Match.Id))
            {
                issues.Add("match.id is required.");
            }

            if (Match.Players is null)
            {
                issues.Add("match.players must not be null (use empty array).");
            }

            if (!System.Enum.IsDefined(typeof(MatchSnapshotStatus), Match.Status))
            {
                issues.Add("match.status is invalid.");
            }
        }

        if (Prop is null)
        {
            issues.Add("Missing prop object.");
        }
        else
        {
            if (Prop.Timestamp == 0)
            {
                issues.Add("prop.timestamp must be greater than zero.");
            }

            if (Prop.UptimeMs is null)
            {
                issues.Add("prop.uptime_ms is required.");
            }
        }

        errors = issues;
        return issues.Count == 0;
    }
}
