using System;
using System.Threading;
using System.Threading.Tasks;
using LaserTag.Defusal.Domain;
using LaserTag.Defusal.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace LaserTag.Defusal.Tests;

public class MatchCoordinatorTests
{
    private static (MatchCoordinator Coordinator, FakeFocusService Focus) CreateCoordinator(MatchOptions? matchOptions = null)
    {
        matchOptions ??= new MatchOptions
        {
            LtDisplayedDurationSec = 400,
            AutoEndNoPlantAtSec = 180,
            DefuseWindowSec = 40,
            ClockExpectedHz = 10
        };

        var focus = new FakeFocusService();
        var relay = new RelayService(Options.Create(new RelayOptions { Enabled = false }), NullLogger<RelayService>.Instance);
        var coordinator = new MatchCoordinator(
            Options.Create(matchOptions),
            relay,
            focus,
            Options.Create(new UiAutomationOptions { DebounceWindowMs = 10 }),
            NullLogger<MatchCoordinator>.Instance);

        return (coordinator, focus);
    }

    [Fact]
    public async Task NoPlantByAutoEnd_TriggersEndMatch()
    {
        var (coordinator, focus) = CreateCoordinator();
        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("alpha", MatchSnapshotStatus.WaitingOnStart, 400_000, 1), CancellationToken.None);
        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("alpha", MatchSnapshotStatus.Running, 220_000, 2), CancellationToken.None);
        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("alpha", MatchSnapshotStatus.Running, (400 - 180) * 1000, 3), CancellationToken.None);

        Assert.Equal(1, focus.TriggerCount);
        Assert.Contains("No plant", focus.LastReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PlantAfter180_AllowsOvertimeAndEndsAfterWindow()
    {
        var (coordinator, focus) = CreateCoordinator();
        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("match", MatchSnapshotStatus.Countdown, 400_000, 1), CancellationToken.None);
        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("match", MatchSnapshotStatus.Running, (400 - 181) * 1000, 2), CancellationToken.None);
        await coordinator.UpdatePropAsync(new PropStatusDto { State = PropState.Planted, Timestamp = 3 }, CancellationToken.None);

        // Before overtime expiry, no trigger.
        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("match", MatchSnapshotStatus.Running, (400 - 200) * 1000, 4), CancellationToken.None);
        Assert.Equal(0, focus.TriggerCount);

        // At plant + 40 (181 + 40 = 221) -> trigger.
        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("match", MatchSnapshotStatus.Running, (400 - 221) * 1000, 5), CancellationToken.None);
        Assert.Equal(1, focus.TriggerCount);
        Assert.Contains("overtime", focus.LastReason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(PropState.Defused, "defused")]
    [InlineData(PropState.Exploded, "exploded")]
    public async Task TerminalPropStatesEndImmediately(PropState propState, string expected)
    {
        var (coordinator, focus) = CreateCoordinator();
        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("match", MatchSnapshotStatus.Running, (400 - 100) * 1000, 1), CancellationToken.None);
        await coordinator.UpdatePropAsync(new PropStatusDto { State = propState, Timestamp = 2 }, CancellationToken.None);

        Assert.Equal(1, focus.TriggerCount);
        Assert.Contains(expected, focus.LastReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FreezetimeIgnoresPropUpdates()
    {
        var (coordinator, focus) = CreateCoordinator();
        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("match", MatchSnapshotStatus.WaitingOnStart, 400_000, 1), CancellationToken.None);
        await coordinator.UpdatePropAsync(new PropStatusDto { State = PropState.Planted, Timestamp = 2 }, CancellationToken.None);
        Assert.Equal(0, focus.TriggerCount);
    }

    [Fact]
    public async Task GameoverLocksOutFurtherActions()
    {
        var (coordinator, focus) = CreateCoordinator();
        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("match", MatchSnapshotStatus.Running, (400 - 190) * 1000, 1), CancellationToken.None);
        await coordinator.UpdatePropAsync(new PropStatusDto { State = PropState.Exploded, Timestamp = 2 }, CancellationToken.None);
        Assert.Equal(1, focus.TriggerCount);

        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("match", MatchSnapshotStatus.Running, (400 - 195) * 1000, 3), CancellationToken.None);
        await coordinator.UpdatePropAsync(new PropStatusDto { State = PropState.Defused, Timestamp = 4 }, CancellationToken.None);
        Assert.Equal(1, focus.TriggerCount);
    }

    [Fact]
    public async Task RemainingTimeConversionProducesExpectedElapsed()
    {
        var (coordinator, _) = CreateCoordinator();
        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("match", MatchSnapshotStatus.Running, (400 - 123) * 1000, 1), CancellationToken.None);
        await coordinator.UpdatePropAsync(new PropStatusDto { State = PropState.Planted, Timestamp = 2 }, CancellationToken.None);
        var snapshot = coordinator.Snapshot();
        Assert.InRange(snapshot.PlantTimeSec ?? double.NaN, 122.5, 123.5);
    }

    private static MatchSnapshotDto NewSnapshot(string id, MatchSnapshotStatus status, int remainingMs, long secondsFromEpoch)
    {
        var timestamp = DateTimeOffset.UnixEpoch.AddSeconds(secondsFromEpoch).UtcDateTime.Ticks;
        return new MatchSnapshotDto
        {
            Id = id,
            Status = status,
            RemainingTimeMs = remainingMs,
            Timestamp = timestamp,
            WinnerTeam = null,
            IsLastSend = false,
            Players = Array.Empty<MatchPlayerSnapshotDto>()
        };
    }

    private sealed class FakeFocusService : IFocusService
    {
        public int TriggerCount { get; private set; }
        public string LastReason { get; private set; } = string.Empty;

        public void BindToUiThread(SynchronizationContext context)
        {
            // No UI thread in tests.
        }

        public Task<FocusActionResult> TryEndMatchAsync(string reason, CancellationToken cancellationToken)
        {
            TriggerCount++;
            LastReason = reason;
            return Task.FromResult(new FocusActionResult(true, reason));
        }

        public Task<FocusActionResult> TryFocusWindowAsync(string reason, CancellationToken cancellationToken)
        {
            LastReason = reason;
            return Task.FromResult(new FocusActionResult(true, reason));
        }

        public FocusWindowInfo GetForegroundWindowInfo()
        {
            return FocusWindowInfo.Empty;
        }
    }
}
