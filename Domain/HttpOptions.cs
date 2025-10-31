namespace LaserTag.Defusal.Domain;

/// <summary>
/// Configuration options governing the embedded HTTP server.
/// </summary>
public sealed class HttpOptions
{
    public string[] Urls { get; set; } = ["http://127.0.0.1:5055"];

    public string? BearerToken { get; set; }

    public string[] AllowedCidrs { get; set; } = ["127.0.0.1/32"];

    public int RequestTimeoutSeconds { get; set; } = 5;
}
