using System;
using System.Collections.Generic;
using System.Linq;
using LaserTag.Defusal.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LaserTag.Defusal.Services;

/// <summary>
/// Provides normalization between prop-reported times and application time.
/// </summary>
public sealed class TimeSynchronizationService
{
    private const int OffsetWindowSize = 20;

    private readonly MatchOptions _options;
    private readonly ILogger<TimeSynchronizationService> _logger;
    private readonly object _sync = new();

    private readonly Queue<long> _offsetWindow = new();
    private TimeSpan _timeOffset = TimeSpan.Zero;
    private DateTimeOffset _lastHeartbeat = DateTimeOffset.MinValue;
    private long? _lastUptimeMs;

    public TimeSynchronizationService(IOptions<MatchOptions> options, ILogger<TimeSynchronizationService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Normalizes an incoming prop timestamp to the application's clock.
    /// </summary>
    /// <param name="timestamp">Unix timestamp (seconds or milliseconds) reported by the prop.</param>
    /// <param name="uptimeMs">Monotonic uptime milliseconds, preferred for synchronization.</param>
    public DateTimeOffset NormalizePropTime(long timestamp, long? uptimeMs)
    {
        var now = DateTimeOffset.UtcNow;
        var inputTimeMs = uptimeMs ?? NormalizeTimestampToMilliseconds(timestamp);

        lock (_sync)
        {
            if (uptimeMs is not null && _lastUptimeMs is not null && uptimeMs.Value < _lastUptimeMs.Value)
            {
                ResetSynchronizationState(
                    now,
                    "Prop uptime decreased from {Previous}ms to {Current}ms; invalidating time offset",
                    _lastUptimeMs,
                    uptimeMs);
            }

            if (_offsetWindow.Count > 0 && (now - _lastHeartbeat).TotalSeconds > _options.PropSessionTimeoutSeconds)
            {
                ResetSynchronizationState(
                    now,
                    "Prop session timed out after {TimeoutSeconds}s; invalidating time offset",
                    _options.PropSessionTimeoutSeconds);
            }

            var offsetMs = now.ToUnixTimeMilliseconds() - inputTimeMs;
            _offsetWindow.Enqueue(offsetMs);

            while (_offsetWindow.Count > OffsetWindowSize)
            {
                _offsetWindow.Dequeue();
            }

            if (_offsetWindow.Count > 0)
            {
                _timeOffset = TimeSpan.FromMilliseconds(MaxOffset());
            }

            _lastHeartbeat = now;
            if (uptimeMs is not null)
            {
                _lastUptimeMs = uptimeMs;
            }

            var normalizedMs = inputTimeMs + (long)_timeOffset.TotalMilliseconds;

            try
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(normalizedMs);
            }
            catch (ArgumentOutOfRangeException)
            {
                return now;
            }
        }
    }

    private void ResetSynchronizationState(DateTimeOffset now, string messageTemplate, params object?[] args)
    {
        _offsetWindow.Clear();
        _timeOffset = TimeSpan.Zero;
        _lastHeartbeat = now;
        _lastUptimeMs = null;
        _logger.LogInformation(messageTemplate, args);
    }

    private long MaxOffset() => _offsetWindow.Max();

    private static long NormalizeTimestampToMilliseconds(long timestamp)
    {
        if (timestamp >= 1_000_000_000_000_000)
        {
            return new DateTimeOffset(new DateTime(timestamp, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
        }

        if (timestamp >= 10_000_000_000)
        {
            return timestamp;
        }

        return timestamp * 1000;
    }
}
