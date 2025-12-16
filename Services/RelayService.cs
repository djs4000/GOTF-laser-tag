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
public sealed class RelayService : IRelayService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RelayService> _logger;
    private readonly IOptionsMonitor<RelayOptions> _optionsMonitor;
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly object _sync = new();
    private RelayStatusSnapshot _status;
    private readonly IDisposable? _optionsReloadToken;

    public RelayService(IOptionsMonitor<RelayOptions> optionsMonitor, ILogger<RelayService> logger)
    {
        _logger = logger;
        _optionsMonitor = optionsMonitor;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        _serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        _serializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        _status = RelayStatusSnapshot.Disabled with { Enabled = IsRelayEnabled(_optionsMonitor.CurrentValue) };
        _optionsReloadToken = _optionsMonitor.OnChange(OnOptionsChanged);
    }

    public event EventHandler<RelayStatusSnapshotEventArgs>? StatusChanged;

    public bool IsEnabled => IsRelayEnabled(_optionsMonitor.CurrentValue);

    public RelayStatusSnapshot Status
    {
        get
        {
            lock (_sync)
            {
                return _status;
            }
        }
    }

    public async Task TryRelayCombinedAsync(CombinedRelayPayload payload, CancellationToken cancellationToken)
    {
        _ = await RelayWithResponseAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RelaySendResult> RelayWithResponseAsync(CombinedRelayPayload payload, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            var disabledResult = new RelaySendResult(false, null, "Relay is disabled via configuration.");
            UpdateStatusOnCompletion(_optionsMonitor.CurrentValue, disabledResult);
            return disabledResult;
        }

        var options = _optionsMonitor.CurrentValue;
        try
        {
            ValidatePayload(payload, options);
        }
        catch (Exception ex)
        {
            var validationResult = new RelaySendResult(false, null, ex.Message);
            UpdateStatusOnCompletion(options, validationResult);
            return validationResult;
        }

        UpdateStatusOnAttemptStarted(options);
        var result = await RelayToUrlAsync(options, payload, cancellationToken).ConfigureAwait(false);
        UpdateStatusOnCompletion(options, result);
        return result;
    }

    private void ValidatePayload(CombinedRelayPayload payload, RelayOptions options)
    {
        if (!payload.TryValidate(out var errors))
        {
            var message = string.Join("; ", errors);
            _logger.LogError("Combined relay payload rejected: {ValidationErrors}", message);
            throw new InvalidOperationException($"Combined relay payload invalid: {message}");
        }

        if (!options.EnableSchemaValidation)
        {
            return;
        }

        _logger.LogDebug("Combined relay payload passed contract validation (schema validation enabled).");
    }

    private async Task<RelaySendResult> RelayToUrlAsync(RelayOptions options, object payload, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, options.Url);
            if (!string.IsNullOrWhiteSpace(options.BearerToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.BearerToken);
            }

            var json = JsonSerializer.Serialize(payload, _serializerOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            _logger.LogInformation("Relaying combined payload to {Url}", options.Url);

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

    private static bool IsRelayEnabled(RelayOptions options)
    {
        return options.Enabled && !string.IsNullOrWhiteSpace(options.Url);
    }

    private void OnOptionsChanged(RelayOptions options)
    {
        UpdateStatus(current => current with
        {
            Enabled = IsRelayEnabled(options),
            IsSending = false
        });
    }

    private void UpdateStatusOnAttemptStarted(RelayOptions options)
    {
        var now = DateTimeOffset.UtcNow;
        UpdateStatus(current => current with
        {
            Enabled = IsRelayEnabled(options),
            IsSending = true,
            LastAttemptUtc = now
        });
    }

    private void UpdateStatusOnCompletion(RelayOptions options, RelaySendResult result, bool recordAttemptTime = true)
    {
        UpdateStatus(current =>
        {
            var attemptTime = recordAttemptTime ? DateTimeOffset.UtcNow : current.LastAttemptUtc;
            return current with
            {
                Enabled = IsRelayEnabled(options),
                IsSending = false,
                LastAttemptUtc = attemptTime,
                LastAttemptSucceeded = recordAttemptTime ? result.Success : current.LastAttemptSucceeded,
                LastStatusCode = recordAttemptTime ? result.StatusCode : current.LastStatusCode,
                LastErrorMessage = recordAttemptTime ? (result.Success ? null : result.ErrorMessage) : current.LastErrorMessage
            };
        });
    }

    private void UpdateStatus(Func<RelayStatusSnapshot, RelayStatusSnapshot> mutator)
    {
        RelayStatusSnapshot updated;
        lock (_sync)
        {
            var candidate = mutator(_status);
            if (candidate.Equals(_status))
            {
                return;
            }

            _status = candidate;
            updated = _status;
        }

        StatusChanged?.Invoke(this, new RelayStatusSnapshotEventArgs(updated));
    }

    public void Dispose()
    {
        _optionsReloadToken?.Dispose();
        _httpClient.Dispose();
    }
}
