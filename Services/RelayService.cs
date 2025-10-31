using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LaserTag.Defusal.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LaserTag.Defusal.Services;

/// <summary>
/// Forwards match state changes to an optional downstream relay endpoint.
/// </summary>
public sealed class RelayService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RelayService> _logger;
    private readonly RelayOptions _options;

    public RelayService(IOptions<RelayOptions> options, ILogger<RelayService> logger)
    {
        _logger = logger;
        _options = options.Value;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
    }

    public bool IsEnabled => _options.Enabled && !string.IsNullOrWhiteSpace(_options.Url);

    public async Task TryRelayAsync(object payload, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _options.Url);
            if (!string.IsNullOrWhiteSpace(_options.BearerToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.BearerToken);
            }

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Relay returned HTTP {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to relay match update");
        }
    }
}
