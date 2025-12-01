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

    public bool IsEnabled => _options.Enabled && (CanRelayMatch || CanRelayProp);

    public bool CanRelayMatch => !string.IsNullOrWhiteSpace(_options.MatchUrl ?? _options.Url);

    public bool CanRelayProp => !string.IsNullOrWhiteSpace(_options.PropUrl ?? _options.Url);

    public Task TryRelayAsync(object payload, CancellationToken cancellationToken)
    {
        return TryRelayMatchAsync(payload, cancellationToken);
    }

    public async Task TryRelayMatchAsync(object payload, CancellationToken cancellationToken)
    {
        await RelayToUrlAsync(_options.MatchUrl ?? _options.Url, payload, cancellationToken).ConfigureAwait(false);
    }

    public async Task TryRelayPropAsync(object payload, CancellationToken cancellationToken)
    {
        await RelayToUrlAsync(_options.PropUrl ?? _options.Url, payload, cancellationToken).ConfigureAwait(false);
    }

    private async Task RelayToUrlAsync(string? url, object payload, CancellationToken cancellationToken)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
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
