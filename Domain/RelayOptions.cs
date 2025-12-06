namespace LaserTag.Defusal.Domain;

/// <summary>
/// Options controlling outbound relay behaviour.
/// </summary>
public sealed class RelayOptions
{
    public bool Enabled { get; set; }

    public string? Url { get; set; }

    public string? BearerToken { get; set; }

    /// <summary>
    /// When true, the relay payload is validated against the combined schema before transmission.
    /// </summary>
    public bool EnableSchemaValidation { get; set; }
}
