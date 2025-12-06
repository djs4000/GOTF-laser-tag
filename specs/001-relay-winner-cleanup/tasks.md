# Tasks: Relay Winner Cleanup

**Input**: Design documents from `/specs/001-relay-winner-cleanup/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests**: Tests are OPTIONAL. Include only where explicitly called out below.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Setup (Shared Infrastructure)

- [X] T001 Confirm build/run baseline for tray app via `dotnet build Application.csproj -c Release`.
- [X] T002 Review `appsettings.json` to ensure Relay configuration defaults to the combined endpoint and document assumptions in specs/001-relay-winner-cleanup/quickstart.md.
- [ ] T003 Run `dotnet test Tests/Tests.csproj` to ensure existing tests pass before feature changes.

---

## Phase 2: Foundational (Blocking Prerequisites)

- [X] T004 Map current relay endpoints and winner logic references across `Http/` handlers, `Services/RelayService.cs`, and `Services/MatchCoordinator.cs` for planned removals/refactors.
- [X] T005 [P] Add detailed logging in `Services/MatchCoordinator.cs` for winner authority decisions and relay payload composition to support validation.
- [X] T006 [P] Verify buffered state stores the most recent match and prop snapshots in `Services/MatchCoordinator.cs` for reuse across relays.
- [X] T007 [P] Audit `Services/FocusService.cs` and `Services/MatchCoordinator.cs` to confirm objective outcomes still invoke Ctrl+S automation against the ICE window and add regression hooks as needed.

---

## Phase 3: User Story 1 - Single Combined Relay (Priority: P1)

**Goal**: Only combined relay endpoint is exposed and used.

**Independent Test**: Post /prop and /match updates; verify only combined relay fires with match+prop data (no legacy endpoints hit).

### Implementation

- [X] T008 [US1] Remove legacy relay endpoint handlers from `Http/` and related routing registrations.
- [X] T009 [US1] Simplify relay configuration in `Services/RelayService.cs` and `appsettings.json` to a single combined endpoint.
- [X] T010 [P] [US1] Update `Program.cs` and any middleware wiring so only the combined relay pipeline is registered.
- [X] T011 [US1] Ensure combined payload serialization in `Domain/CombinedPayloadDto.cs` (or equivalent) prevents null/empty match and prop sections.

### Validation

- [X] T012 [US1] Extend `Tests/MatchCoordinatorTests.cs` with a case where only /prop updates arrive and assert the combined relay payload includes the latest match snapshot.
- [X] T013 [US1] Extend `Tests/MatchCoordinatorTests.cs` with a case that verifies no legacy relay endpoint is invoked (e.g., assert only one relay URL is used).

---

## Phase 4: User Story 2 - Correct Winner Determination (Priority: P1)

**Goal**: Winner honors host team wipe if prior to objective; otherwise objective override or time expiration applies while triggering focus automation.

**Independent Test**: Simulate host Completed with winner due to wipe before prop resolution; simulate explode/defuse overrides; simulate timeout no-plant → defenders; verify Ctrl+S fires on objective outcomes.

### Implementation

- [X] T014 [US2] Encode winner authority precedence (HostTeamWipe > Objective outcomes > TimeExpiration) within `Services/MatchCoordinator.cs`.
- [X] T015 [US2] Populate `winner_team` and `winner_reason` fields in relay DTOs (`Domain/CombinedPayloadDto.cs` and `Services/RelayService.cs`).
- [X] T016 [P] [US2] Guard against host completion events overriding an already resolved objective outcome within `Services/MatchCoordinator.cs`.
- [X] T017 [US2] Preserve FSM timing enforcement (180s no-plant auto-end, 40s overtime) while applying winner outcomes in `Services/MatchCoordinator.cs`.
- [X] T018 [P] [US2] Update `Ui/MatchResultForm.cs` (or equivalent) to display the final winner and winner_reason consistent with relay payloads.
- [X] T019 [US2] Ensure `Services/FocusService.cs` is invoked from `Services/MatchCoordinator.cs` whenever objective outcomes finalize the match (Ctrl+S automation).

### Validation

- [X] T020 [US2] Add host team-wipe test coverage to `Tests/MatchCoordinatorTests.cs`, asserting winner_team stays aligned with host WinnerTeam when resolved before objective events.
- [X] T021 [US2] Add prop explode/defuse override tests to `Tests/MatchCoordinatorTests.cs`, verifying winner_team/winner_reason reflect objective outcomes and that host values are ignored.
- [X] T022 [US2] Add no-plant timeout test plus countdown-ignore coverage to `Tests/MatchCoordinatorTests.cs`, asserting defenders win and countdown prop events are ignored.
- [ ] T023 [US2] Create `scripts/focus-validation.ps1` (or update existing harness) to simulate objective outcomes and confirm logs (`logs/focus.log`) show Ctrl+S automation triggered.
- [X] T024 [US2] Add assertions in `Tests/MatchCoordinatorTests.cs` (or separate test fixture) that winner_reason is populated for every end-of-match scenario, satisfying SC-004.

---

## Phase 5: User Story 3 - Deterministic Relay Content (Priority: P2)

**Goal**: Every relay payload includes latest buffered match and prop objects (no null components).

**Independent Test**: Alternate /match and /prop at different cadences; each relay has both sections populated with last-known data.

### Implementation

- [X] T025 [US3] Ensure buffering path in `Services/MatchCoordinator.cs` persists last-known match snapshot when /prop triggers the relay.
- [X] T026 [US3] Ensure buffering path in `Services/MatchCoordinator.cs` persists last-known prop status when /match triggers the relay.
- [X] T027 [P] [US3] Align serialization with `contracts/combined-relay.json`, adding schema validation hooks in `Services/RelayService.cs` if needed.

### Validation

- [X] T028 [US3] Add high-frequency /match vs low-frequency /prop cadence test in `Tests/MatchCoordinatorTests.cs`, ensuring relay payload always includes the last prop state.
- [X] T029 [US3] Add high-frequency /prop vs low-frequency /match cadence test in `Tests/MatchCoordinatorTests.cs`, ensuring relay payload always includes the last match snapshot.
- [ ] T030 [US3] Run contract validation using `contracts/combined-relay.json` via a schema check script (`scripts/schema/validate-combined-relay.ps1`).

---

## Phase N: Polish & Cross-Cutting Concerns

- [ ] T031 [P] Update specs/001-relay-winner-cleanup/quickstart.md with the new validation scenarios (focus automation, winner reasons, cadence tests).
- [ ] T032 [P] Tune diagnostics settings in `appsettings.json` to capture relay payload and focus automation logs at Information level.
- [ ] T033 [P] Execute `dotnet test Tests/Tests.csproj` plus scenario scripts (`scripts/focus-validation.ps1`, `scripts/schema/validate-combined-relay.ps1`) and archive artifacts.
- [ ] T034 [P] Verify publishing remains single-file win-x64 (check `Application.csproj` publish profile) and confirm tray UI remains always-on-top after changes.
- [ ] T035 [P] Add or update performance harness `scripts/perf/relay-latency.ps1` to measure dispatch time; ensure relays fire within sub-second SLA and capture results in `logs/perf.log`.

---

## Dependencies & Execution Order

- Foundational (Phase 2) blocks all user stories.
- US1 and US2 (both P1) should proceed sequentially: complete relay consolidation before modifying winner logic/focus automation; US3 depends on buffered state changes from prior phases.
- Validation tasks rely on the tests/scripts introduced within the same story phase.

## Parallel Example: User Story 1

- Tasks T008 and T009 touch different files and can proceed in parallel once T004–T007 complete.
- T010 can run alongside T011 after configuration updates, while tests T012–T013 run once implementation is ready.

## Implementation Strategy

- MVP First: Complete US1 to deliver combined relay-only behavior with deterministic payload contents.
- Incremental Delivery: After US1, finish US2 to correct winner determination and focus automation; then deliver US3 deterministic cadence handling, followed by polish/performance validation.
