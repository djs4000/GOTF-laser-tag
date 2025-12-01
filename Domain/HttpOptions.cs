namespace LaserTag.Defusal.Domain;

/// <summary>
/// Configuration options governing the embedded HTTP server.
/// </summary>
public sealed class HttpOptions
{
    public string[] Urls { get; set; } = [""];

    public string? BearerToken { get; set; }

    public string[] AllowedCidrs { get; set; } = ["192.168.1.0/24", "192.168.0.0/24"];

    public int RequestTimeoutSeconds { get; set; } = 5;
}
