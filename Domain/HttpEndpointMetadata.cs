using System.Linq;

namespace LaserTag.Defusal.Domain;

/// <summary>
/// Provides read-only access to the HTTP endpoints the application is bound to.
/// </summary>
public sealed class HttpEndpointMetadata
{
    public HttpEndpointMetadata(IEnumerable<string> urls)
    {
        Urls = (urls ?? Array.Empty<string>()).ToArray();
    }

    /// <summary>
    /// Gets the resolved HTTP URLs that the embedded server is bound to.
    /// </summary>
    public IReadOnlyList<string> Urls { get; }
}
