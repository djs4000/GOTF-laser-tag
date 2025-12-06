using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LaserTag.Defusal.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LaserTag.Defusal.Services;

/// <summary>
/// Forwards combined match+prop payloads to the downstream relay endpoint.
/// </summary>
public sealed class RelayService : IRelayService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RelayService> _logger;
    private readonly RelayOptions _options;
    private readonly JsonSerializerOptions _serializerOptions;

    public RelayService(IOptions<RelayOptions> options, ILogger<RelayService> logger)
    {
        _logger = logger;
        _options = options.Value;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        _serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        _serializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
    }

    public bool IsEnabled => _options.Enabled && !string.IsNullOrWhiteSpace(_options.Url);

    public async Task TryRelayCombinedAsync(CombinedRelayPayload payload, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return;
        }

        ValidatePayload(payload);

        await RelayToUrlAsync(_options.Url!, payload, cancellationToken).ConfigureAwait(false);
    }

    private void ValidatePayload(CombinedRelayPayload payload)
    {
        if (!_options.EnableSchemaValidation)
        {
            return;
        }

        if (payload.Match is null)
        {
            throw new InvalidOperationException("Combined relay payload must include match data.");
        }

        if (payload.Prop is null)
        {
            throw new InvalidOperationException("Combined relay payload must include prop data.");
        }

        if (payload.Match.Players is null)
        {
            throw new InvalidOperationException("Combined relay payload must populate match players (use empty array when absent).");
        }
    }

    private async Task RelayToUrlAsync(string url, object payload, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            if (!string.IsNullOrWhiteSpace(_options.BearerToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.BearerToken);
            }

            var json = JsonSerializer.Serialize(payload, _serializerOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            _logger.LogInformation("Relaying combined payload to {Url}", url);

            var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Relay returned HTTP {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to relay combined payload");
        }
    }
}
