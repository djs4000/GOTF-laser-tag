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
        await coordinator.UpdatePropAsync(new PropStatusDto { State = PropState.Armed, Timestamp = 3 }, CancellationToken.None);

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
    [InlineData(PropState.Detonated, "detonated")]
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
        await coordinator.UpdatePropAsync(new PropStatusDto { State = PropState.Armed, Timestamp = 2 }, CancellationToken.None);
        Assert.Equal(0, focus.TriggerCount);
    }

    [Fact]
    public async Task PropStateIsCapturedForDisplayWhileInactive()
    {
        var (coordinator, focus) = CreateCoordinator();
        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("match", MatchSnapshotStatus.WaitingOnStart, 400_000, 1), CancellationToken.None);
        await coordinator.UpdatePropAsync(new PropStatusDto { State = PropState.Armed, Timestamp = 2 }, CancellationToken.None);

        var snapshot = coordinator.Snapshot();
        Assert.Equal(PropState.Armed, snapshot.PropState);
        Assert.Equal(0, focus.TriggerCount);
    }

    [Fact]
    public async Task GameoverLocksOutFurtherActions()
    {
        var (coordinator, focus) = CreateCoordinator();
        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("match", MatchSnapshotStatus.Running, (400 - 190) * 1000, 1), CancellationToken.None);
        await coordinator.UpdatePropAsync(new PropStatusDto { State = PropState.Detonated, Timestamp = 2 }, CancellationToken.None);
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
        await coordinator.UpdatePropAsync(new PropStatusDto { State = PropState.Armed, Timestamp = 2 }, CancellationToken.None);
        var snapshot = coordinator.Snapshot();
        Assert.InRange(snapshot.PlantTimeSec ?? double.NaN, 122.5, 123.5);
    }

    [Fact]
    public async Task BuildPropResponseReflectsLatestMatchStatus()
    {
        var (coordinator, _) = CreateCoordinator();
        const int secondsFromEpoch = 10;
        var matchTimestamp = DateTimeOffset.UnixEpoch.AddSeconds(secondsFromEpoch).UtcDateTime.Ticks;
        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("match", MatchSnapshotStatus.Running, 200_000, secondsFromEpoch), CancellationToken.None);
        await coordinator.UpdatePropAsync(new PropStatusDto { State = PropState.Armed, Timestamp = matchTimestamp + 1 }, CancellationToken.None);

        var response = coordinator.BuildPropResponse(matchTimestamp + 2);

        Assert.Equal(MatchLifecycleState.Running.ToString(), response.Status);
        Assert.Equal(200_000, response.RemainingTimeMs);
        Assert.Equal(matchTimestamp, response.Timestamp);
    }

    [Fact]
    public async Task IgnoresUnexpectedMatchIdWhileActive()
    {
        var (coordinator, _) = CreateCoordinator();
        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("alpha", MatchSnapshotStatus.Running, 200_000, 1), CancellationToken.None);

        // Incoming snapshot claims a different match id but represents a running game; it should be ignored.
        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("beta", MatchSnapshotStatus.Running, 199_000, 2), CancellationToken.None);

        var snapshot = coordinator.Snapshot();
        Assert.Equal("alpha", snapshot.MatchId);
        Assert.Equal(MatchLifecycleState.Running, snapshot.LifecycleState);
        Assert.Equal(200_000, snapshot.RemainingTimeMs);
    }

    [Fact]
    public async Task MaintainsPropStateDuringWaitingOnStartSnapshots()
    {
        var (coordinator, _) = CreateCoordinator();
        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("match", MatchSnapshotStatus.WaitingOnStart, 400_000, 1), CancellationToken.None);
        await coordinator.UpdatePropAsync(new PropStatusDto { State = PropState.Ready, Timestamp = 2 }, CancellationToken.None);

        // A follow-up snapshot in the same state should not reset the prop back to Idle.
        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("match", MatchSnapshotStatus.WaitingOnStart, 399_000, 3), CancellationToken.None);

        var snapshot = coordinator.Snapshot();
        Assert.Equal(PropState.Ready, snapshot.PropState);
    }

    [Fact]
    public async Task ParsesUnixSecondTimestampsForLatency()
    {
        var (coordinator, _) = CreateCoordinator();
        var unixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var dto = new MatchSnapshotDto
        {
            Id = "match",
            Status = MatchSnapshotStatus.Running,
            RemainingTimeMs = 200_000,
            Timestamp = unixSeconds,
            WinnerTeam = null,
            IsLastSend = false,
            Players = Array.Empty<MatchPlayerSnapshotDto>()
        };

        await coordinator.UpdateMatchSnapshotAsync(dto, CancellationToken.None);
        var latency = coordinator.Snapshot().LastClockLatency;

        Assert.NotNull(latency);
        Assert.InRange(latency!.Value.TotalMilliseconds, 2, 5_000);
    }

    [Fact]
    public async Task FutureClockTimestampsAreClampedToMinimumLatency()
    {
        var (coordinator, _) = CreateCoordinator();
        var futureTimestamp = DateTimeOffset.UtcNow.AddSeconds(2).ToUnixTimeMilliseconds();
        var dto = new MatchSnapshotDto
        {
            Id = "match",
            Status = MatchSnapshotStatus.Running,
            RemainingTimeMs = 200_000,
            Timestamp = futureTimestamp,
            WinnerTeam = null,
            IsLastSend = false,
            Players = Array.Empty<MatchPlayerSnapshotDto>()
        };

        await coordinator.UpdateMatchSnapshotAsync(dto, CancellationToken.None);
        var latency = coordinator.Snapshot().LastClockLatency;

        Assert.Equal(TimeSpan.FromMilliseconds(2), latency);
    }

    [Fact]
    public async Task AllowsNewMatchAfterTerminalState()
    {
        var (coordinator, _) = CreateCoordinator();
        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("alpha", MatchSnapshotStatus.Completed, 0, 1), CancellationToken.None);

        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("beta", MatchSnapshotStatus.WaitingOnStart, 400_000, 2), CancellationToken.None);

        var snapshot = coordinator.Snapshot();
        Assert.Equal("beta", snapshot.MatchId);
        Assert.Equal(MatchLifecycleState.WaitingOnStart, snapshot.LifecycleState);
        Assert.Equal(400_000, snapshot.RemainingTimeMs);
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
