using System;
using LaserTag.Defusal.Domain;
using Microsoft.Extensions.Logging;

namespace LaserTag.Defusal.Services;

/// <summary>
/// Buffers the latest inbound payloads and most recent outbound CombinedRelayPayload published by
/// MatchCoordinator so the Relay Monitor UI can render stable JSON even when the coordinator is idle.
/// </summary>
public sealed class RelaySnapshotCache : IDisposable
{
    private readonly MatchCoordinator _coordinator;
    private readonly ILogger<RelaySnapshotCache> _logger;
    private readonly object _sync = new();
    private readonly TimeSpan _staleThreshold = TimeSpan.FromSeconds(5);
    private CombinedRelayPayload? _latestOutboundPayload;
    private DateTimeOffset? _lastOutboundUtc;
    private bool _disposed;
    private bool? _lastStaleState;
    private MatchSnapshotDto? _lastInboundMatch;
    private PropStatusDto? _lastInboundProp;

    public RelaySnapshotCache(MatchCoordinator coordinator, ILogger<RelaySnapshotCache> logger)
    {
        _coordinator = coordinator;
        _logger = logger;
        _coordinator.SnapshotUpdated += OnSnapshotUpdated;
    }

    public RelaySnapshotState GetSnapshot()
    {
        lock (_sync)
        {
            return BuildSnapshotUnsafe();
        }
    }

    private void OnSnapshotUpdated(object? sender, MatchStateSnapshot snapshot)
    {
        RelaySnapshotState state;
        bool shouldPublish;
        lock (_sync)
        {
            var previousStaleState = _lastStaleState;
            var outboundPayload = snapshot.LatestOutboundRelayPayload;
            var isNewOutbound = !ReferenceEquals(outboundPayload, _latestOutboundPayload);

            _lastInboundMatch = snapshot.LatestInboundMatchPayload ?? _lastInboundMatch;
            _lastInboundProp = snapshot.LatestInboundPropPayload ?? _lastInboundProp;

            if (isNewOutbound)
            {
                _latestOutboundPayload = outboundPayload;
                _lastOutboundUtc = snapshot.LatestOutboundAt ?? DateTimeOffset.UtcNow;
            }

            state = BuildSnapshotUnsafe();
            var staleChanged = previousStaleState is null || previousStaleState.Value != state.IsStale;
            shouldPublish = isNewOutbound || staleChanged || snapshot.LatestInboundMatchPayload is not null || snapshot.LatestInboundPropPayload is not null;
        }

        if (shouldPublish)
        {
            if (snapshot.LatestOutboundRelayPayload is not null)
            {
                _logger.LogDebug("Cached outbound relay payload timestamp {Timestamp}.", snapshot.LatestOutboundRelayPayload.Timestamp);
            }

            SnapshotChanged?.Invoke(this, new RelaySnapshotEventArgs(state));
        }
    }

    private RelaySnapshotState BuildSnapshotUnsafe()
    {
        var now = DateTimeOffset.UtcNow;
        var stale = !_lastOutboundUtc.HasValue || now - _lastOutboundUtc.Value > _staleThreshold;
        if (_lastStaleState is null || _lastStaleState.Value != stale)
        {
            _lastStaleState = stale;
            if (stale)
            {
                _logger.LogWarning("Relay payload has gone stale (> {ThresholdSeconds}s without outbound updates).", _staleThreshold.TotalSeconds);
            }
            else
            {
                _logger.LogInformation("Relay payload refreshed at {UpdatedAt:u}.", _lastOutboundUtc);
            }
        }

        return new RelaySnapshotState(
            OutboundPayload: _latestOutboundPayload,
            LastUpdatedUtc: _lastOutboundUtc,
            IsStale: stale,
            LastInboundMatch: _lastInboundMatch,
            LastInboundProp: _lastInboundProp);
    }

    public event EventHandler<RelaySnapshotEventArgs>? SnapshotChanged;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _coordinator.SnapshotUpdated -= OnSnapshotUpdated;
    }
}
