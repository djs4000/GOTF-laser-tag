namespace LaserTag.Defusal.Domain;

/// <summary>
/// Options controlling outbound relay behaviour.
/// </summary>
public sealed class RelayOptions
{
    public bool Enabled { get; set; }

    /// <summary>
    /// Legacy single-endpoint destination. Used as a fallback for match or prop URLs when they are not set.
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// Destination for match/clock payloads.
    /// </summary>
    public string? MatchUrl { get; set; }

    /// <summary>
    /// Destination for prop payloads.
    /// </summary>
    public string? PropUrl { get; set; }

    public string? BearerToken { get; set; }
}
