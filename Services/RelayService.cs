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
        _ = await RelayWithResponseAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RelaySendResult> RelayWithResponseAsync(CombinedRelayPayload payload, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return new RelaySendResult(false, null, "Relay is disabled via configuration.");
        }

        try
        {
            ValidatePayload(payload);
        }
        catch (Exception ex)
        {
            return new RelaySendResult(false, null, ex.Message);
        }

        return await RelayToUrlAsync(_options.Url!, payload, cancellationToken).ConfigureAwait(false);
    }

    private void ValidatePayload(CombinedRelayPayload payload)
    {
        if (!payload.TryValidate(out var errors))
        {
            var message = string.Join("; ", errors);
            _logger.LogError("Combined relay payload rejected: {ValidationErrors}", message);
            throw new InvalidOperationException($"Combined relay payload invalid: {message}");
        }

        if (!_options.EnableSchemaValidation)
        {
            return;
        }

        _logger.LogDebug("Combined relay payload passed contract validation (schema validation enabled).");
    }

    private async Task<RelaySendResult> RelayToUrlAsync(string url, object payload, CancellationToken cancellationToken)
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
                return new RelaySendResult(false, (int)response.StatusCode, $"Relay returned HTTP {(int)response.StatusCode}");
            }

            return new RelaySendResult(true, (int)response.StatusCode, "Relay succeeded.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to relay combined payload");
            return new RelaySendResult(false, null, ex.Message);
        }
    }
}
