using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LaserTag.Defusal.Domain;
using LaserTag.Defusal.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Sdk;

namespace LaserTag.Defusal.Tests;

public class MatchCoordinatorTests
{
    private static readonly CombinedRelaySchemaValidator SchemaValidator = CombinedRelaySchemaValidator.LoadDefault();

    private static (MatchCoordinator Coordinator, DeterministicFocusService Focus, DeterministicRelayService Relay) CreateCoordinator(MatchOptions? matchOptions = null)
    {
        matchOptions ??= new MatchOptions
        {
            LtDisplayedDurationSec = 400,
            AutoEndNoPlantAtSec = 180,
            DefuseWindowSec = 40,
            ClockExpectedHz = 10
        };

        var focus = new DeterministicFocusService();
        var relay = new DeterministicRelayService();
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

    private static void AssertValidCombinedPayload(CombinedRelayPayload payload)
    {
        SchemaValidator.AssertValid(payload);
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
        AssertValidCombinedPayload(payload);
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
        AssertValidCombinedPayload(payload);
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
        AssertValidCombinedPayload(payload);
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
        AssertValidCombinedPayload(payload);
        Assert.NotNull(payload.Prop);
        Assert.Equal("charlie", payload.Match.Id);
    }

    [Fact]
    public async Task PropOnlySequenceKeepsBufferedMatchSnapshot()
    {
        var (coordinator, _, relay) = CreateCoordinator();
        relay.IsEnabled = true;

        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("sequence", MatchSnapshotStatus.Running, 210_000, 1), CancellationToken.None);
        await coordinator.UpdatePropAsync(new PropStatusDto { State = PropState.Armed, Timestamp = 2 }, CancellationToken.None);
        await coordinator.UpdatePropAsync(new PropStatusDto { State = PropState.Active, Timestamp = 3 }, CancellationToken.None);

        Assert.True(relay.Payloads.Count >= 2, "Expected at least the two prop-triggered relay payloads.");
        Assert.All(relay.Payloads, payload =>
        {
            AssertValidCombinedPayload(payload);
            Assert.Equal("sequence", payload.Match.Id);
        });
        Assert.Contains(relay.Payloads, payload => payload.Prop.State == PropState.Armed);
        Assert.Contains(relay.Payloads, payload => payload.Prop.State == PropState.Active);
    }

    [Fact]
    public async Task AlternatingMatchAndPropUpdatesUseCombinedRelay()
    {
        var (coordinator, _, relay) = CreateCoordinator();
        relay.IsEnabled = true;

        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("delta", MatchSnapshotStatus.Running, 205_000, 1), CancellationToken.None);
        await coordinator.UpdatePropAsync(new PropStatusDto { State = PropState.Armed, Timestamp = 2 }, CancellationToken.None);
        await coordinator.UpdateMatchSnapshotAsync(NewSnapshot("delta", MatchSnapshotStatus.Running, 204_000, 3), CancellationToken.None);
        await coordinator.UpdatePropAsync(new PropStatusDto { State = PropState.Active, Timestamp = 4 }, CancellationToken.None);

        Assert.True(relay.Payloads.Count >= 4, "Expected at least the alternating match/prop relay payloads.");
        Assert.All(relay.Payloads, payload =>
        {
            AssertValidCombinedPayload(payload);
            Assert.NotNull(payload.Match);
            Assert.NotNull(payload.Prop);
        });
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

    private sealed class DeterministicFocusService : IFocusService
    {
        private readonly object _sync = new();
        private readonly List<string> _reasons = new();
        private TaskCompletionSource<int> _triggerSignal = CreateSignal();

        public int TriggerCount { get; private set; }
        public string LastReason { get; private set; } = string.Empty;

        public IReadOnlyList<string> Reasons
        {
            get
            {
                lock (_sync)
                {
                    return _reasons.ToArray();
                }
            }
        }

        public Task WaitForTriggersAsync(int expectedCount, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            Task<int> waiter;
            lock (_sync)
            {
                if (TriggerCount >= expectedCount)
                {
                    return Task.CompletedTask;
                }

                waiter = _triggerSignal.Task;
            }

            return waiter.WaitAsync(timeout, cancellationToken);
        }

        private static TaskCompletionSource<int> CreateSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void BindToUiThread(SynchronizationContext context)
        {
            // Tests run without a WinForms context; no-op.
        }

        public Task<FocusActionResult> TryEndMatchAsync(string reason, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                TriggerCount++;
                LastReason = reason;
                _reasons.Add(reason);
                _triggerSignal.TrySetResult(TriggerCount);
                _triggerSignal = CreateSignal();
            }

            return Task.FromResult(new FocusActionResult(true, reason));
        }

        public Task<FocusActionResult> TryFocusWindowAsync(string reason, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                LastReason = reason;
            }

            return Task.FromResult(new FocusActionResult(true, reason));
        }

        public FocusWindowInfo GetForegroundWindowInfo()
        {
            return FocusWindowInfo.Empty;
        }
    }

    private sealed class DeterministicRelayService : IRelayService
    {
        private readonly object _sync = new();
        private TaskCompletionSource<int> _payloadSignal = CreateSignal();

        public bool IsEnabled { get; set; }

        public List<CombinedRelayPayload> Payloads { get; } = new();

        public TimeSpan ArtificialDelay { get; set; } = TimeSpan.Zero;

        public async Task TryRelayCombinedAsync(CombinedRelayPayload payload, CancellationToken cancellationToken)
        {
            if (!IsEnabled)
            {
                return;
            }

            var snapshot = ClonePayload(payload);

            lock (_sync)
            {
                Payloads.Add(snapshot);
                _payloadSignal.TrySetResult(Payloads.Count);
                _payloadSignal = CreateSignal();
            }

            if (ArtificialDelay > TimeSpan.Zero)
            {
                await Task.Delay(ArtificialDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        public Task WaitForPayloadsAsync(int expectedCount, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            Task<int> waiter;
            lock (_sync)
            {
                if (Payloads.Count >= expectedCount)
                {
                    return Task.CompletedTask;
                }

                waiter = _payloadSignal.Task;
            }

            return waiter.WaitAsync(timeout, cancellationToken);
        }

        private static TaskCompletionSource<int> CreateSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private static CombinedRelayPayload ClonePayload(CombinedRelayPayload payload)
        {
            if (payload.Match is null)
            {
                throw new XunitException("Relay payload missing match content during clone.");
            }

            if (payload.Prop is null)
            {
                throw new XunitException("Relay payload missing prop content during clone.");
            }

            return new CombinedRelayPayload
            {
                Timestamp = payload.Timestamp,
                WinnerTeam = payload.WinnerTeam,
                WinnerReason = payload.WinnerReason,
                Match = CloneMatch(payload.Match),
                Prop = CloneProp(payload.Prop)
            };
        }

        private static MatchSnapshotDto CloneMatch(MatchSnapshotDto match)
        {
            return new MatchSnapshotDto
            {
                Id = match.Id,
                Timestamp = match.Timestamp,
                IsLastSend = match.IsLastSend,
                Status = match.Status,
                RemainingTimeMs = match.RemainingTimeMs,
                WinnerTeam = match.WinnerTeam,
                Players = match.Players is { Count: > 0 } ? match.Players.ToArray() : Array.Empty<MatchPlayerSnapshotDto>()
            };
        }

        private static PropStatusDto CloneProp(PropStatusDto prop)
        {
            return new PropStatusDto
            {
                Timestamp = prop.Timestamp,
                State = prop.State,
                TimerMs = prop.TimerMs,
                UptimeMs = prop.UptimeMs
            };
        }
    }

    private sealed class CombinedRelaySchemaValidator
    {
        private readonly HashSet<string> _rootRequired;
        private readonly HashSet<string> _matchRequired;
        private readonly HashSet<string> _propRequired;
        private readonly string _schemaPath;

        private CombinedRelaySchemaValidator(
            string schemaPath,
            HashSet<string> rootRequired,
            HashSet<string> matchRequired,
            HashSet<string> propRequired)
        {
            _schemaPath = schemaPath;
            _rootRequired = rootRequired;
            _matchRequired = matchRequired;
            _propRequired = propRequired;
        }

        public static CombinedRelaySchemaValidator LoadDefault()
        {
            var schemaPath = ResolveSchemaPath();

            using var document = JsonDocument.Parse(File.ReadAllText(schemaPath));
            var root = document.RootElement;
            var properties = root.GetProperty("properties");

            return new CombinedRelaySchemaValidator(
                schemaPath,
                ExtractRequired(root),
                ExtractRequired(properties.GetProperty("match")),
                ExtractRequired(properties.GetProperty("prop")));
        }

        private static HashSet<string> ExtractRequired(JsonElement element)
        {
            if (!element.TryGetProperty("required", out var requiredArray))
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in requiredArray.EnumerateArray())
            {
                var name = entry.GetString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    set.Add(name);
                }
            }

            return set;
        }

        public void AssertValid(CombinedRelayPayload payload)
        {
            if (payload is null)
            {
                throw new XunitException($"Combined relay payload is null (schema: {_schemaPath}).");
            }

            if (_rootRequired.Contains("timestamp") && payload.Timestamp <= 0)
            {
                throw new XunitException("Combined relay payload timestamp must be greater than zero.");
            }

            if (_rootRequired.Contains("match"))
            {
                ValidateMatch(payload.Match);
            }

            if (_rootRequired.Contains("prop"))
            {
                ValidateProp(payload.Prop);
            }
        }

        private void ValidateMatch(MatchSnapshotDto? match)
        {
            if (match is null)
            {
                throw new XunitException($"Schema {_schemaPath} requires a match object.");
            }

            if (_matchRequired.Contains("id") && string.IsNullOrWhiteSpace(match.Id))
            {
                throw new XunitException("match.id is required by the combined relay schema.");
            }

            if (_matchRequired.Contains("status") && !Enum.IsDefined(typeof(MatchSnapshotStatus), match.Status))
            {
                throw new XunitException("match.status must be a valid MatchSnapshotStatus.");
            }

            if (_matchRequired.Contains("is_last_send"))
            {
                _ = match.IsLastSend;
            }

            if (match.Players is null)
            {
                throw new XunitException("match.players must not be null.");
            }
        }

        private void ValidateProp(PropStatusDto? prop)
        {
            if (prop is null)
            {
                throw new XunitException($"Schema {_schemaPath} requires a prop object.");
            }

            if (_propRequired.Contains("state") && !Enum.IsDefined(typeof(PropState), prop.State))
            {
                throw new XunitException("prop.state must be a valid PropState.");
            }

            if (_propRequired.Contains("timestamp") && prop.Timestamp <= 0)
            {
                throw new XunitException("prop.timestamp must be greater than zero.");
            }

            if (_propRequired.Contains("uptime_ms") && prop.UptimeMs is null)
            {
                throw new XunitException("prop.uptime_ms must be supplied.");
            }
        }

        private static string ResolveSchemaPath()
        {
            var relativePath = Path.Combine("specs", "001-relay-winner-cleanup", "contracts", "combined-relay.json");
            foreach (var root in EnumerateProbeRoots())
            {
                var candidate = Path.Combine(root, relativePath);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }

            var fallback = Path.Combine(AppContext.BaseDirectory, relativePath);
            throw new FileNotFoundException("Combined relay schema not found.", Path.GetFullPath(fallback));
        }

        private static IEnumerable<string> EnumerateProbeRoots()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            {
                if (string.IsNullOrWhiteSpace(start))
                {
                    continue;
                }

                var current = new DirectoryInfo(start);
                while (current is not null)
                {
                    if (seen.Add(current.FullName))
                    {
                        yield return current.FullName;
                    }

                    current = current.Parent;
                }
            }
        }
    }
}
