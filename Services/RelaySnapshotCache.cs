using System;
using LaserTag.Defusal.Domain;
using Microsoft.Extensions.Logging;

namespace LaserTag.Defusal.Services;

/// <summary>
/// Buffers the most recent CombinedRelayPayload published by MatchCoordinator.SnapshotUpdated so the
/// Relay Monitor UI can render stable JSON even when the coordinator is idle.
/// </summary>
public sealed class RelaySnapshotCache : IDisposable
{
    private readonly MatchCoordinator _coordinator;
    private readonly ILogger<RelaySnapshotCache> _logger;
    private readonly object _sync = new();
    private readonly TimeSpan _staleThreshold = TimeSpan.FromSeconds(5);
    private CombinedRelayPayload? _latestPayload;
    private DateTimeOffset? _lastUpdatedUtc;
    private bool _disposed;
    private bool? _lastStaleState;

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
        var payload = snapshot.LatestCombinedRelayPayload;
        if (payload is null)
        {
            return;
        }

        RelaySnapshotState state;
        lock (_sync)
        {
            _latestPayload = payload;
            _lastUpdatedUtc = DateTimeOffset.UtcNow;
            state = BuildSnapshotUnsafe();
        }
        _logger.LogDebug("Cached combined relay payload timestamp {Timestamp}.", payload.Timestamp);
        SnapshotChanged?.Invoke(this, new RelaySnapshotEventArgs(state));
    }

    private RelaySnapshotState BuildSnapshotUnsafe()
    {
        var now = DateTimeOffset.UtcNow;
        var stale = !_lastUpdatedUtc.HasValue || now - _lastUpdatedUtc.Value > _staleThreshold;
        if (_lastStaleState is null || _lastStaleState.Value != stale)
        {
            _lastStaleState = stale;
            if (stale)
            {
                _logger.LogWarning("Relay payload has gone stale (> {ThresholdSeconds}s without updates).", _staleThreshold.TotalSeconds);
            }
            else
            {
                _logger.LogInformation("Relay payload refreshed at {UpdatedAt:u}.", _lastUpdatedUtc);
            }
        }

        return new RelaySnapshotState(_latestPayload, _lastUpdatedUtc, stale);
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
