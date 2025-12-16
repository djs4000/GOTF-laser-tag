using System;
using System.Text.Json;
using LaserTag.Defusal.Domain;
using Microsoft.Extensions.Logging;

namespace LaserTag.Defusal.Services;

/// <summary>
/// Validates operator-crafted payloads and sends them through the existing relay stack.
/// </summary>
public sealed class DebugPayloadService
{
    private readonly IRelayService _relayService;
    private readonly RelaySnapshotCache _snapshotCache;
    private readonly ILogger<DebugPayloadService> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public DebugPayloadService(
        IRelayService relayService,
        RelaySnapshotCache snapshotCache,
        ILogger<DebugPayloadService> logger)
    {
        _relayService = relayService;
        _snapshotCache = snapshotCache;
        _logger = logger;
    }

    public async Task<DebugPayloadResult> SendAsync(DebugPayloadType payloadType, string jsonBody, CancellationToken cancellationToken)
    {
        if (!_relayService.IsEnabled)
        {
            throw new DebugPayloadValidationException("Relay is disabled. Enable Relay in Settings before sending debug payloads.");
        }

        if (string.IsNullOrWhiteSpace(jsonBody))
        {
            throw new DebugPayloadValidationException("JSON body cannot be empty.");
        }

        CombinedRelayPayload combinedPayload = payloadType switch
        {
            DebugPayloadType.Combined => ParseCombined(jsonBody),
            DebugPayloadType.Match => MergeMatchPayload(jsonBody),
            DebugPayloadType.Prop => MergePropPayload(jsonBody),
            _ => throw new DebugPayloadValidationException("Unsupported payload type.")
        };

        if (!combinedPayload.TryValidate(out var validationErrors))
        {
            throw new DebugPayloadValidationException($"Combined payload invalid: {string.Join("; ", validationErrors)}");
        }

        _logger.LogInformation("Sending debug payload type {PayloadType}", payloadType);
        var result = await _relayService.RelayWithResponseAsync(combinedPayload, cancellationToken).ConfigureAwait(false);
        var message = result.Success
            ? $"Relay accepted payload (HTTP {result.StatusCode ?? 200})."
            : $"Relay failed: {result.ErrorMessage ?? "Unknown error"}";

        _logger.LogInformation("Debug payload send result: {Message}", message);
        return new DebugPayloadResult(result.Success, result.StatusCode, message);
    }

    private CombinedRelayPayload ParseCombined(string jsonBody)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<CombinedRelayPayload>(jsonBody, _jsonOptions);
            if (payload is null)
            {
                throw new DebugPayloadValidationException("Combined payload JSON did not deserialize into a payload object.");
            }

            return payload;
        }
        catch (JsonException ex)
        {
            throw new DebugPayloadValidationException($"Combined payload JSON invalid: {ex.Message}");
        }
    }

    private CombinedRelayPayload MergeMatchPayload(string jsonBody)
    {
        MatchSnapshotDto match;
        try
        {
            match = JsonSerializer.Deserialize<MatchSnapshotDto>(jsonBody, _jsonOptions)
                     ?? throw new DebugPayloadValidationException("Match payload JSON was empty.");
        }
        catch (JsonException ex)
        {
            throw new DebugPayloadValidationException($"Match payload JSON invalid: {ex.Message}");
        }

        var snapshot = _snapshotCache.GetSnapshot();
        var fallback = snapshot.LastInboundProp ?? snapshot.OutboundPayload?.Prop ?? BuildPlaceholderProp();
        return new CombinedRelayPayload
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            AttackingTeam = ResolveAttackingTeam(),
            WinnerReason = null,
            Match = match,
            Prop = fallback
        };
    }

    private CombinedRelayPayload MergePropPayload(string jsonBody)
    {
        PropStatusDto prop;
        try
        {
            prop = JsonSerializer.Deserialize<PropStatusDto>(jsonBody, _jsonOptions)
                   ?? throw new DebugPayloadValidationException("Prop payload JSON was empty.");
        }
        catch (JsonException ex)
        {
            throw new DebugPayloadValidationException($"Prop payload JSON invalid: {ex.Message}");
        }

        var snapshot = _snapshotCache.GetSnapshot();
        var fallback = snapshot.LastInboundMatch ?? snapshot.OutboundPayload?.Match ?? BuildPlaceholderMatch();
        return new CombinedRelayPayload
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            AttackingTeam = ResolveAttackingTeam(),
            WinnerReason = null,
            Match = fallback,
            Prop = prop
        };
    }

    private string ResolveAttackingTeam()
    {
        var snapshot = _snapshotCache.GetSnapshot();
        return snapshot.OutboundPayload?.AttackingTeam ?? "Team 1";
    }

    private static PropStatusDto BuildPlaceholderProp()
    {
        return new PropStatusDto
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            State = PropState.Idle,
            TimerMs = 0,
            UptimeMs = 0
        };
    }

    private static MatchSnapshotDto BuildPlaceholderMatch()
    {
        return new MatchSnapshotDto
        {
            Id = $"debug-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Status = MatchSnapshotStatus.Running,
            RemainingTimeMs = 60000,
            WinnerTeam = null,
            Players = Array.Empty<MatchPlayerSnapshotDto>()
        };
    }
}
