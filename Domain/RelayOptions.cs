namespace LaserTag.Defusal.Domain;

/// <summary>
/// Options controlling outbound relay behaviour.
/// </summary>
public sealed class RelayOptions
{
    public bool Enabled { get; set; }

    public string? Url { get; set; }

    public string? BearerToken { get; set; }
}
