using System;
using LaserTag.Defusal.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LaserTag.Defusal.Services;

/// <summary>
/// Provides normalization between prop-reported times and application time.
/// </summary>
public sealed class TimeSynchronizationService
{
    private readonly MatchOptions _options;
    private readonly ILogger<TimeSynchronizationService> _logger;
    private readonly object _sync = new();

    private bool _isSynced;
    private TimeSpan _timeOffset = TimeSpan.Zero;
    private DateTimeOffset _lastHeartbeat = DateTimeOffset.MinValue;

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
            if (_isSynced && (now - _lastHeartbeat).TotalSeconds > _options.PropSessionTimeoutSeconds)
            {
                _isSynced = false;
                _logger.LogInformation(
                    "Prop session timed out after {TimeoutSeconds}s; invalidating time offset",
                    _options.PropSessionTimeoutSeconds);
            }

            if (!_isSynced)
            {
                var offsetMs = now.ToUnixTimeMilliseconds() - inputTimeMs;
                _timeOffset = TimeSpan.FromMilliseconds(offsetMs);
                _isSynced = true;
                _logger.LogInformation("New Prop session established. Offset: {Offset}ms", offsetMs);
            }

            _lastHeartbeat = now;
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
