# Tasks: Relay Winner Cleanup

**Input**: Design documents from `D:\Documents\Code Project\GOTF-laser-tag\specs\001-relay-winner-cleanup\`

**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/

**Tests**: Only add/execute tests explicitly listed below.

**Organization**: Tasks are grouped by user story so each slice can be implemented, tested, and delivered independently.

## Format: `[ID] [P?] [Story] Description`

`[P]` = parallelizable task (no dependency conflicts). `[US#]` labels user-story tasks. Include absolute file paths in every description.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Validate scope and baseline build before modifying code.

- [X] T001 Review governing requirements in `D:\Documents\Code Project\GOTF-laser-tag\agents.md`, `D:\Documents\Code Project\GOTF-laser-tag\specs\001-relay-winner-cleanup\plan.md`, and `D:\Documents\Code Project\GOTF-laser-tag\specs\001-relay-winner-cleanup\spec.md` to ensure upcoming changes align with AGENTS.md.
- [X] T002 [P] Run baseline `dotnet build` + `dotnet test` for `D:\Documents\Code Project\GOTF-laser-tag\Application.csproj` and `D:\Documents\Code Project\GOTF-laser-tag\Tests\Tests.csproj`, archiving logs referenced by quickstart validation.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared helpers and fixtures required by all user stories.

- [X] T003 Create a reusable combined relay schema validator that loads `D:\Documents\Code Project\GOTF-laser-tag\specs\001-relay-winner-cleanup\contracts\combined-relay.json` and expose helper methods inside `D:\Documents\Code Project\GOTF-laser-tag\Tests\MatchCoordinatorTests.cs`.
- [X] T004 [P] Build deterministic relay/focus service test doubles inside `D:\Documents\Code Project\GOTF-laser-tag\Tests\MatchCoordinatorTests.cs` to simulate asynchronous /prop and /match cadences for later story coverage.

---

## Phase 3: User Story 1 - Single Combined Relay (Priority: P1)

**Goal**: Downstream systems only receive the combined relay endpoint, and every dispatch bundles the latest match + prop snapshots.

**Independent Test**: Post alternating `/prop` and `/match` payloads; verify only the combined relay URL fires and every payload contains populated `match` and `prop` objects even if only one source updated.

### Tests for User Story 1

- [X] T005 [P] [US1] Add regression test in `D:\Documents\Code Project\GOTF-laser-tag\Tests\MatchCoordinatorTests.cs` that sends only `/prop` updates and asserts the combined relay payload still embeds the buffered match snapshot.
- [X] T006 [P] [US1] Add regression test in `D:\Documents\Code Project\GOTF-laser-tag\Tests\MatchCoordinatorTests.cs` to confirm alternating `/match` and `/prop` updates never trigger legacy relay endpoints (assert the stub relay receives a single combined URL).

### Implementation for User Story 1

- [X] T007 [US1] Remove match-only/prop-only relay artifacts by deleting `D:\Documents\Code Project\GOTF-laser-tag\Domain\MatchRelayDto.cs` and stripping any remaining references in `D:\Documents\Code Project\GOTF-laser-tag\Services\RelayService.cs` and `D:\Documents\Code Project\GOTF-laser-tag\Program.cs`, adding inline comments clarifying combined-only behavior.
- [X] T008 [P] [US1] Enforce combined payload contract validation inside `D:\Documents\Code Project\GOTF-laser-tag\Services\RelayService.cs` and `D:\Documents\Code Project\GOTF-laser-tag\Domain\CombinedRelayPayload.cs`, ensuring match/prop objects and player collections are never null and logging schema violations.
- [X] T009 [P] [US1] Harden buffering inside `D:\Documents\Code Project\GOTF-laser-tag\Services\MatchCoordinator.cs` so `BuildCombinedPayloadLocked` clones the latest match and prop DTOs with fallback timestamps even when one source has not reported, documenting cadence handling per AGENTS.md.
- [ ] T010 [US1] Update operator configuration/docs in `D:\Documents\Code Project\GOTF-laser-tag\appsettings.json` and `D:\Documents\Code Project\GOTF-laser-tag\specs\001-relay-winner-cleanup\quickstart.md` to describe the single combined relay endpoint and remove references to deprecated relays.

---

## Phase 4: User Story 2 - Correct Winner Determination (Priority: P1)

**Goal**: Winner logic honors host team wipes only before objective resolution, otherwise objective (detonate/defuse) or time expiration outcomes override, and focus automation triggers during objective endings.

**Independent Test**: Simulate (1) host Completed with WinnerTeam prior to any prop resolution, (2) prop detonation/defuse after host winner to ensure override, and (3) timeout with no plant to award defenders—each scenario should populate `winner_team`, `winner_reason`, and trigger Ctrl+S when objectives resolve.

### Tests for User Story 2

- [ ] T011 [P] [US2] Add host team-wipe test case inside `D:\Documents\Code Project\GOTF-laser-tag\Tests\MatchCoordinatorTests.cs` verifying `winner_team` mirrors the host WinnerTeam when Completed arrives before prop events.
- [ ] T012 [P] [US2] Add prop detonation/defuse override tests to `D:\Documents\Code Project\GOTF-laser-tag\Tests\MatchCoordinatorTests.cs`, asserting host winners are superseded and the focus-service stub records a Ctrl+S invocation.
- [ ] T013 [P] [US2] Add timeout and overtime coverage in `D:\Documents\Code Project\GOTF-laser-tag\Tests\MatchCoordinatorTests.cs` ensuring defenders win when no plant occurs by 180 s and attackers win when the overtime defuse window expires.

### Implementation for User Story 2

- [ ] T014 [US2] Implement winner precedence logic in `D:\Documents\Code Project\GOTF-laser-tag\Services\MatchCoordinator.cs`, allowing HostTeamWipe winners only before objective resolution and enabling objective/time authorities to override via `SetWinnerLocked`.
- [ ] T015 [P] [US2] Extend `D:\Documents\Code Project\GOTF-laser-tag\Domain\MatchStateSnapshot.cs` and `D:\Documents\Code Project\GOTF-laser-tag\Ui\MatchResultForm.cs` so winner roles and reasons surface consistently (including CombinedRelayPayload winner_reason metadata).
- [ ] T016 [P] [US2] Ensure focus automation remains wired by verifying `TryEndMatchAsync` in `D:\Documents\Code Project\GOTF-laser-tag\Services\MatchCoordinator.cs` invokes `D:\Documents\Code Project\GOTF-laser-tag\Services\FocusService.cs` whenever prop outcomes resolve the match, adding descriptive logs.
- [ ] T017 [US2] Refine FSM enforcement in `D:\Documents\Code Project\GOTF-laser-tag\Services\MatchCoordinator.cs` so countdown prop events are ignored, time-expiration winners annotate context, and `_winnerReason` always matches AGENTS.md authority order.

---

## Phase 5: User Story 3 - Deterministic Relay Content (Priority: P2)

**Goal**: Every relay payload contains sanitized, last-known match and prop objects even under cadence mismatches or when only one source has reported.

**Independent Test**: Run alternating cadence simulations (e.g., 10 Hz /match vs 2 Hz /prop) and one-sided source scenarios; verify 200 consecutive relays contain fully populated match + prop sections with stable winner metadata.

### Tests for User Story 3

- [ ] T018 [P] [US3] Add high-frequency `/match` vs low-frequency `/prop` test in `D:\Documents\Code Project\GOTF-laser-tag\Tests\MatchCoordinatorTests.cs` asserting each relay payload carries the latest prop snapshot.
- [ ] T019 [P] [US3] Add single-source + alternating-edge test in `D:\Documents\Code Project\GOTF-laser-tag\Tests\MatchCoordinatorTests.cs` verifying CombinedRelayPayload substitutes deterministic defaults instead of emitting null sections when one source is absent.

### Implementation for User Story 3

- [ ] T020 [US3] Introduce immutable buffer copies within `D:\Documents\Code Project\GOTF-laser-tag\Services\MatchCoordinator.cs` so relays serialize sanitized DTOs (no shared mutable references), documenting the buffering strategy inline.
- [ ] T021 [P] [US3] Surface the latest CombinedRelayPayload for operators by enhancing `D:\Documents\Code Project\GOTF-laser-tag\Ui\StatusForm.cs` and `D:\Documents\Code Project\GOTF-laser-tag\Ui\MatchResultForm.cs` to display timestamped payload JSON, winner_team, and winner_reason.
- [ ] T022 [US3] Invoke the schema validator from T003 inside `D:\Documents\Code Project\GOTF-laser-tag\Services\RelayService.cs` before HTTP dispatch and log failures to `D:\Documents\Code Project\GOTF-laser-tag\logs\combined-relay.log`.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, validation, and packaging checks after story delivery.

- [ ] T023 [P] Refresh operator docs in `D:\Documents\Code Project\GOTF-laser-tag\specs\001-relay-winner-cleanup\quickstart.md` and `D:\Documents\Code Project\GOTF-laser-tag\specs\001-relay-winner-cleanup\research.md` with the new winner precedence notes, schema validation steps, and cadence scenarios.
- [ ] T024 [P] Execute the quickstart validation scenarios from `D:\Documents\Code Project\GOTF-laser-tag\specs\001-relay-winner-cleanup\quickstart.md`, capturing relay/focus logs under `D:\Documents\Code Project\GOTF-laser-tag\logs\` for audit.
- [ ] T025 [P] Verify publishing still produces a single-file win-x64 build by running `dotnet publish` on `D:\Documents\Code Project\GOTF-laser-tag\Application.csproj` and spot-checking the tray UI behavior post-changes.
- [ ] T026 Investigate and resolve the outstanding latency-related test failures (`ParsesUnixSecondTimestampsForLatency`, `FutureClockTimestampsClampToZeroLatency`, `FuturePropTimestampsClampToZeroLatency`, `LatencySnapshotsPublishWhenWindowCompletes`) in `D:\Documents\Code Project\GOTF-laser-tag\Tests\MatchCoordinatorTests.cs`.

---

## Dependencies & Execution Order

- Phase 1 precedes all work; Phase 2 helpers block every story.
- User Story 1 (Phase 3) delivers the MVP and must finish before User Story 2 modifies winner logic, which in turn must finish before User Story 3 reuses the finalized buffers.
- Polish (Phase 6) depends on all targeted user stories being completed.

---

## Parallel Execution Examples

- **US1**: After T003–T004, tasks T005 and T006 (test scaffolding) can run in parallel, while T008 and T009 modify different files and may proceed concurrently once T007 lands.
- **US2**: T011–T013 are independent test cases; T015 (UI updates) can run alongside T016 (focus automation) because they touch distinct areas after T014 completes.
- **US3**: T018 and T019 can be authored simultaneously; T021 (UI) and T022 (RelayService) can run in parallel once T020 finalizes buffer cloning.

---

## Implementation Strategy

1. **MVP First**: Complete Setup + Foundational phases, then finish User Story 1 to deliver combined-relay-only behavior—this is the MVP scope.
2. **Incremental Delivery**: Layer User Story 2 winner precedence refactors atop the MVP, validate, then implement User Story 3 deterministic buffering before entering the polish phase.
3. **Testing Cadence**: For each story, land the designated regression tests first (T005–T006, T011–T013, T018–T019) so implementation changes can be validated immediately.

---
