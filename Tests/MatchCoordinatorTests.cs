using System;
using System.Collections.Generic;
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
    private static (MatchCoordinator Coordinator, FakeFocusService Focus, TestRelayService Relay) CreateCoordinator(MatchOptions? matchOptions = null)
    {
        matchOptions ??= new MatchOptions
        {
            LtDisplayedDurationSec = 400,
            AutoEndNoPlantAtSec = 180,
            DefuseWindowSec = 40,
            ClockExpectedHz = 10
        };

        var focus = new FakeFocusService();
        var relay = new TestRelayService();
        var timeSync = new TimeSynchronizationService(Options.Create(matchOptions), NullLogger<TimeSynchronizationService>.Instance);
        var coordinator = new MatchCoordinator(
            Options.Create(matchOptions),
            relay,
            focus,
            Options.Create(new UiAutomationOptions { DebounceWindowMs = 10 }),
            timeSync,
            NullLogger<MatchCoordinator>.Instance);

        return (coordinator, focus, relay);
    }

    [Fact]
    public async Task NoPlantByAutoEnd_TriggersEndMatch()
    {
        var (coordinator, focus, _) = CreateCoordinator();
        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("alpha", MatchSnapshotStatus.WaitingOnStart, 400_000, 1), CancellationToken.None);
        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("alpha", MatchSnapshotStatus.Running, 220_000, 2), CancellationToken.None);
        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("alpha", MatchSnapshotStatus.Running, (400 - 180) * 1000, 3), CancellationToken.None);

        Assert.Equal(1, focus.TriggerCount);
        Assert.Contains("No plant", focus.LastReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PlantAfter180_AllowsOvertimeAndEndsAfterWindow()
    {
        var (coordinator, focus, _) = CreateCoordinator();
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
        var (coordinator, focus, _) = CreateCoordinator();
        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("match", MatchSnapshotStatus.Running, (400 - 100) * 1000, 1), CancellationToken.None);
        await coordinator.UpdatePropAsync(new PropStatusDto { State = propState, Timestamp = 2 }, CancellationToken.None);

        Assert.Equal(1, focus.TriggerCount);
        Assert.Contains(expected, focus.LastReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FreezetimeIgnoresPropUpdates()
    {
        var (coordinator, focus, _) = CreateCoordinator();
        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("match", MatchSnapshotStatus.WaitingOnStart, 400_000, 1), CancellationToken.None);
        await coordinator.UpdatePropAsync(new PropStatusDto { State = PropState.Armed, Timestamp = 2 }, CancellationToken.None);
        Assert.Equal(0, focus.TriggerCount);
    }

    [Fact]
    public async Task PropStateIsCapturedForDisplayWhileInactive()
    {
        var (coordinator, focus, _) = CreateCoordinator();
        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("match", MatchSnapshotStatus.WaitingOnStart, 400_000, 1), CancellationToken.None);
        await coordinator.UpdatePropAsync(new PropStatusDto { State = PropState.Armed, Timestamp = 2 }, CancellationToken.None);

        var snapshot = coordinator.Snapshot();
        Assert.Equal(PropState.Armed, snapshot.PropState);
        Assert.Equal(0, focus.TriggerCount);
    }

    [Fact]
    public async Task GameoverLocksOutFurtherActions()
    {
        var (coordinator, focus, _) = CreateCoordinator();
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
        var (coordinator, _, _) = CreateCoordinator();
        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("match", MatchSnapshotStatus.Running, (400 - 123) * 1000, 1), CancellationToken.None);
        await coordinator.UpdatePropAsync(new PropStatusDto { State = PropState.Armed, Timestamp = 2 }, CancellationToken.None);
        var snapshot = coordinator.Snapshot();
        Assert.InRange(snapshot.PlantTimeSec ?? double.NaN, 122.5, 123.5);
    }

    [Fact]
    public async Task BuildPropResponseReflectsLatestMatchStatus()
    {
        var (coordinator, _, _) = CreateCoordinator();
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
    public async Task PropUpdatesIncludeLatestMatchSnapshotInCombinedPayload()
    {
        var (coordinator, _, relay) = CreateCoordinator();
        relay.IsEnabled = true;

        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("alpha", MatchSnapshotStatus.Running, 200_000, 1), CancellationToken.None);
        await coordinator.UpdatePropAsync(new PropStatusDto { State = PropState.Armed, Timestamp = 2 }, CancellationToken.None);

        Assert.NotEmpty(relay.Payloads);
        var payload = relay.Payloads[^1];
        Assert.Equal("alpha", payload.Match.Id);
        Assert.Equal(PropState.Armed, payload.Prop.State);
    }

    [Fact]
    public async Task MatchUpdatesIncludeBufferedPropState()
    {
        var (coordinator, _, relay) = CreateCoordinator();
        relay.IsEnabled = true;

        await coordinator.UpdatePropAsync(new PropStatusDto { State = PropState.Armed, Timestamp = 1 }, CancellationToken.None);
        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("bravo", MatchSnapshotStatus.Running, 199_000, 2), CancellationToken.None);

        Assert.NotEmpty(relay.Payloads);
        var payload = relay.Payloads[^1];
        Assert.Equal("bravo", payload.Match.Id);
        Assert.Equal(PropState.Armed, payload.Prop.State);
    }

    [Fact]
    public async Task PropOnlyUpdatesStillIncludeMatchDefaults()
    {
        var (coordinator, _, relay) = CreateCoordinator();
        relay.IsEnabled = true;

        await coordinator.UpdatePropAsync(new PropStatusDto { State = PropState.Armed, Timestamp = 1 }, CancellationToken.None);

        Assert.NotEmpty(relay.Payloads);
        var payload = relay.Payloads[^1];
        Assert.False(string.IsNullOrWhiteSpace(payload.Match.Id));
        Assert.NotNull(payload.Match);
    }

    [Fact]
    public async Task MatchOnlyUpdatesStillIncludePropDefaults()
    {
        var (coordinator, _, relay) = CreateCoordinator();
        relay.IsEnabled = true;

        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("charlie", MatchSnapshotStatus.Running, 200_000, 1), CancellationToken.None);

        Assert.NotEmpty(relay.Payloads);
        var payload = relay.Payloads[^1];
        Assert.NotNull(payload.Prop);
        Assert.Equal("charlie", payload.Match.Id);
    }

    [Fact]
    public async Task IgnoresUnexpectedMatchIdWhileActive()
    {
        var (coordinator, _, _) = CreateCoordinator();
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
        var (coordinator, _, _) = CreateCoordinator();
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
        var options = new MatchOptions
        {
            LtDisplayedDurationSec = 400,
            AutoEndNoPlantAtSec = 180,
            DefuseWindowSec = 40,
            ClockExpectedHz = 10,
            LatencyWindow = 1
        };
        var (coordinator, _, _) = CreateCoordinator(options);
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
        var latency = coordinator.Snapshot().ClockLatency;

        Assert.NotNull(latency);
        Assert.InRange(latency!.Average.TotalMilliseconds, 0, 5_000);
        Assert.InRange(latency.Minimum.TotalMilliseconds, 0, 5_000);
        Assert.InRange(latency.Maximum.TotalMilliseconds, 0, 5_000);
        Assert.Equal(1, latency.SampleCount);
    }

    [Fact]
    public async Task FutureClockTimestampsClampToZeroLatency()
    {
        var options = new MatchOptions
        {
            LtDisplayedDurationSec = 400,
            AutoEndNoPlantAtSec = 180,
            DefuseWindowSec = 40,
            ClockExpectedHz = 10,
            LatencyWindow = 1
        };
        var (coordinator, _, _) = CreateCoordinator(options);
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
        var latency = coordinator.Snapshot().ClockLatency;

        Assert.NotNull(latency);
        Assert.Equal(TimeSpan.Zero, latency!.Average);
        Assert.Equal(TimeSpan.Zero, latency.Minimum);
        Assert.Equal(TimeSpan.Zero, latency.Maximum);
    }

    [Fact]
    public async Task ComputesPropLatencyFromUnixSeconds()
    {
        var options = new MatchOptions
        {
            LtDisplayedDurationSec = 400,
            AutoEndNoPlantAtSec = 180,
            DefuseWindowSec = 40,
            ClockExpectedHz = 10,
            LatencyWindow = 1
        };
        var (coordinator, _, _) = CreateCoordinator(options);
        var propTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("match", MatchSnapshotStatus.Running, 200_000, propTimestamp), CancellationToken.None);
        await coordinator.UpdatePropAsync(new PropStatusDto { State = PropState.Armed, Timestamp = propTimestamp }, CancellationToken.None);

        var latency = coordinator.Snapshot().PropLatency;

        Assert.NotNull(latency);
        Assert.InRange(latency!.Average.TotalMilliseconds, 0, 5_000);
        Assert.InRange(latency.Minimum.TotalMilliseconds, 0, 5_000);
        Assert.InRange(latency.Maximum.TotalMilliseconds, 0, 5_000);
        Assert.Equal(1, latency.SampleCount);
    }

    [Fact]
    public async Task FuturePropTimestampsClampToZeroLatency()
    {
        var options = new MatchOptions
        {
            LtDisplayedDurationSec = 400,
            AutoEndNoPlantAtSec = 180,
            DefuseWindowSec = 40,
            ClockExpectedHz = 10,
            LatencyWindow = 1
        };
        var (coordinator, _, _) = CreateCoordinator(options);
        var futureTimestamp = DateTimeOffset.UtcNow.AddSeconds(2).ToUnixTimeMilliseconds();

        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("match", MatchSnapshotStatus.Running, 200_000, DateTimeOffset.UtcNow.ToUnixTimeSeconds()), CancellationToken.None);
        await coordinator.UpdatePropAsync(new PropStatusDto { State = PropState.Armed, Timestamp = futureTimestamp }, CancellationToken.None);

        var latency = coordinator.Snapshot().PropLatency;

        Assert.NotNull(latency);
        Assert.Equal(TimeSpan.Zero, latency!.Average);
        Assert.Equal(TimeSpan.Zero, latency.Minimum);
        Assert.Equal(TimeSpan.Zero, latency.Maximum);
    }

    [Fact]
    public async Task LatencySnapshotsPublishWhenWindowCompletes()
    {
        var options = new MatchOptions
        {
            LtDisplayedDurationSec = 400,
            AutoEndNoPlantAtSec = 180,
            DefuseWindowSec = 40,
            ClockExpectedHz = 10,
            LatencyWindow = 3
        };
        var (coordinator, _, _) = CreateCoordinator(options);

        var baseTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("match", MatchSnapshotStatus.Running, 200_000, baseTimestamp), CancellationToken.None);
        Assert.Null(coordinator.Snapshot().ClockLatency);

        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("match", MatchSnapshotStatus.Running, 199_000, baseTimestamp + 1), CancellationToken.None);
        Assert.Null(coordinator.Snapshot().ClockLatency);

        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("match", MatchSnapshotStatus.Running, 198_000, baseTimestamp + 2), CancellationToken.None);
        var latency = coordinator.Snapshot().ClockLatency;

        Assert.NotNull(latency);
        Assert.Equal(3, latency!.SampleCount);
    }

    [Fact]
    public async Task PropTimerCountsDownBetweenUpdates()
    {
        var (coordinator, _, _) = CreateCoordinator();
        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("match", MatchSnapshotStatus.Running, 200_000, 1), CancellationToken.None);

        await coordinator.UpdatePropAsync(new PropStatusDto { State = PropState.Armed, Timestamp = 2, TimerMs = 40_000 }, CancellationToken.None);
        var initial = coordinator.Snapshot().PropTimerRemainingMs;

        Assert.NotNull(initial);
        Assert.InRange(initial!.Value, 39_000, 40_000);

        await Task.Delay(120);

        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("match", MatchSnapshotStatus.Running, 199_000, 3), CancellationToken.None);
        var updated = coordinator.Snapshot().PropTimerRemainingMs;

        Assert.NotNull(updated);
        Assert.True(updated!.Value < initial.Value);
        Assert.InRange(updated.Value, initial.Value - 400, initial.Value);
    }

    [Fact]
    public async Task PropTimerStaysFrozenWhenNotArmed()
    {
        var (coordinator, _, _) = CreateCoordinator();
        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("match", MatchSnapshotStatus.Running, 200_000, 1), CancellationToken.None);

        await coordinator.UpdatePropAsync(new PropStatusDto { State = PropState.Active, Timestamp = 2, TimerMs = 30_000 }, CancellationToken.None);
        var initial = coordinator.Snapshot().PropTimerRemainingMs;

        Assert.NotNull(initial);
        Assert.InRange(initial!.Value, 29_000, 30_000);

        await Task.Delay(120);

        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("match", MatchSnapshotStatus.Running, 199_000, 3), CancellationToken.None);
        var updated = coordinator.Snapshot().PropTimerRemainingMs;

        Assert.NotNull(updated);
        Assert.Equal(initial.Value, updated!.Value);
    }

    [Fact]
    public async Task AllowsNewMatchAfterTerminalState()
    {
        var (coordinator, _, _) = CreateCoordinator();
        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("alpha", MatchSnapshotStatus.Completed, 0, 1), CancellationToken.None);

        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("beta", MatchSnapshotStatus.WaitingOnStart, 400_000, 2), CancellationToken.None);

        var snapshot = coordinator.Snapshot();
        Assert.Equal("beta", snapshot.MatchId);
        Assert.Equal(MatchLifecycleState.WaitingOnStart, snapshot.LifecycleState);
        Assert.Equal(400_000, snapshot.RemainingTimeMs);
    }

    [Fact]
    public async Task HostWinnerBeforeObjectiveIsRespected()
    {
        var (coordinator, _, _) = CreateCoordinator();
        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("alpha", MatchSnapshotStatus.Running, 200_000, 1), CancellationToken.None);
        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("alpha", MatchSnapshotStatus.Completed, 0, 2, winnerTeam: "Team 2"), CancellationToken.None);

        var snapshot = coordinator.Snapshot();
        Assert.Equal("Team 2", snapshot.WinnerTeam);
        Assert.Equal(WinnerReason.HostTeamWipe, snapshot.WinnerReason);
    }

    [Fact]
    public async Task ObjectiveOutcomeOverridesLaterHostWinner()
    {
        var (coordinator, _, _) = CreateCoordinator();
        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("alpha", MatchSnapshotStatus.Running, 200_000, 1), CancellationToken.None);
        await coordinator.UpdatePropAsync(new PropStatusDto { State = PropState.Detonated, Timestamp = 2 }, CancellationToken.None);
        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("alpha", MatchSnapshotStatus.Completed, 0, 3, winnerTeam: "Team 2"), CancellationToken.None);

        var snapshot = coordinator.Snapshot();
        Assert.Equal("Team 1", snapshot.WinnerTeam); // Default attacking team
        Assert.Equal(WinnerReason.ObjectiveDetonated, snapshot.WinnerReason);
    }

    [Fact]
    public async Task NoPlantTimeoutAwardsDefenders()
    {
        var (coordinator, _, _) = CreateCoordinator();
        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("alpha", MatchSnapshotStatus.Running, (400 - 100) * 1000, 1), CancellationToken.None);
        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("alpha", MatchSnapshotStatus.Running, (400 - 181) * 1000, 2), CancellationToken.None);

        var snapshot = coordinator.Snapshot();
        Assert.Equal("Team 2", snapshot.WinnerTeam);
        Assert.Equal(WinnerReason.TimeExpiration, snapshot.WinnerReason);
    }

    [Fact]
    public async Task PropDefuseSetsWinnerReason()
    {
        var (coordinator, _, _) = CreateCoordinator();
        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("alpha", MatchSnapshotStatus.Running, (400 - 10) * 1000, 1), CancellationToken.None);
        await coordinator.UpdatePropAsync(new PropStatusDto { State = PropState.Defused, Timestamp = 2 }, CancellationToken.None);

        var snapshot = coordinator.Snapshot();
        Assert.Equal("Team 2", snapshot.WinnerTeam);
        Assert.Equal(WinnerReason.ObjectiveDefused, snapshot.WinnerReason);
    }

    [Fact]
    public async Task PropDetonationSetsWinnerReason()
    {
        var (coordinator, _, _) = CreateCoordinator();
        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("alpha", MatchSnapshotStatus.Running, (400 - 10) * 1000, 1), CancellationToken.None);
        await coordinator.UpdatePropAsync(new PropStatusDto { State = PropState.Detonated, Timestamp = 2 }, CancellationToken.None);

        var snapshot = coordinator.Snapshot();
        Assert.Equal("Team 1", snapshot.WinnerTeam);
        Assert.Equal(WinnerReason.ObjectiveDetonated, snapshot.WinnerReason);
    }

    private static MatchSnapshotDto NewSnapshot(string id, MatchSnapshotStatus status, int remainingMs, long secondsFromEpoch, string? winnerTeam = null, bool isLastSend = false)
    {
        var timestamp = DateTimeOffset.UnixEpoch.AddSeconds(secondsFromEpoch).UtcDateTime.Ticks;
        return new MatchSnapshotDto
        {
            Id = id,
            Status = status,
            RemainingTimeMs = remainingMs,
            Timestamp = timestamp,
            WinnerTeam = winnerTeam,
            IsLastSend = isLastSend,
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

    private sealed class TestRelayService : IRelayService
    {
        public bool IsEnabled { get; set; }

        public List<CombinedRelayPayload> Payloads { get; } = new();

        public Task TryRelayCombinedAsync(CombinedRelayPayload payload, CancellationToken cancellationToken)
        {
            Payloads.Add(payload);
            return Task.CompletedTask;
        }
    }
}
