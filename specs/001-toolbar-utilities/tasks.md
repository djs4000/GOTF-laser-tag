# Tasks: Toolbar Utilities Enhancements

**Input**: Design documents from `/specs/001-toolbar-utilities/`
**Prerequisites**: plan.md, spec.md, research.md (2.md), data-model.md, contracts/

**Tests**: Automated tests are not explicitly required by the spec; focus on manual validation steps from quickstart.md unless future stories demand automated coverage.

**Organization**: Tasks are grouped by user story so each increment can ship independently.

**Commentary Requirement**: Every code task must add or refresh explanatory comments per AGENTS.md, especially around toolbar UX, validation, relay buffering, and automation behaviors.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Task can proceed in parallel (different files, no dependency)
- **[Story]**: Label maps to the applicable user story (US1, US2, US3)
- Include absolute file paths in every description

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Validate constitutional constraints and understand existing configuration before building toolbar utilities.

- [X] T001 Review AGENTS requirements and annotate impacted rules directly in D:\Documents\Code Project\GOTF-laser-tag\agents.md before changing UI, relay, or automation behaviors.
- [X] T002 Inventory every configuration section and validation constraint by walking through D:\Documents\Code Project\GOTF-laser-tag\appsettings.json to prepare the Settings form data model.
- [X] T003 Capture insertion points for toolbar controls and preflight messaging by documenting notes in D:\Documents\Code Project\GOTF-laser-tag\Ui\StatusForm.cs.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Establish shared services that all toolbar utilities depend on.

- [X] T004 Refactor D:\Documents\Code Project\GOTF-laser-tag\Program.cs and D:\Documents\Code Project\GOTF-laser-tag\Ui\TrayApplicationContext.cs so WinForms forms can resolve via DI, enabling toolbar buttons to spawn Settings, Relay Monitor, and Debug windows consistently.
- [X] T005 Implement a centralized navigation helper with restoration/focus logic inside D:\Documents\Code Project\GOTF-laser-tag\Services\ToolbarNavigationService.cs to keep the StatusForm on top whenever utilities open.
- [X] T006 Create a relay snapshot cache and event hook in D:\Documents\Code Project\GOTF-laser-tag\Services\RelaySnapshotCache.cs that subscribes to MatchCoordinator updates and stores the latest CombinedRelayPayload for downstream UI consumers.

---

## Phase 3: User Story 1 - Toolbar Access to Configuration (Priority: P1)

**Goal**: Provide an always-on toolbar that opens a Settings form capable of editing every appsettings.json option with inline validation and restart prompts; remove the obsolete preflight match-length check.

**Independent Test**: Launch the coordinator, open Settings via the toolbar, edit Match.AutoEndNoPlantAtSec, save, and confirm the value persists without triggering a match-length preflight error.

### Implementation for User Story 1

- [X] T007 [US1] Add a ToolStrip-based toolbar with Settings button, tooltips, and keyboard shortcuts to D:\Documents\Code Project\GOTF-laser-tag\Ui\StatusForm.cs so operators can launch utilities without obscuring the form.
- [X] T008 [US1] Introduce strongly typed ApplicationSettingsProfile view-models that mirror every configuration section inside D:\Documents\Code Project\GOTF-laser-tag\Domain\ApplicationSettingsProfileViewModel.cs for UI binding.
- [X] T009 [US1] Build a configuration persistence service in D:\Documents\Code Project\GOTF-laser-tag\Services\SettingsPersistenceService.cs that loads/saves appsettings.json, enforces option validation, and flags restart-required sections.
- [X] T010 [US1] Create the Settings UI with grouped sections, inline validators, reset/cancel actions, and restart prompts in D:\Documents\Code Project\GOTF-laser-tag\Ui\SettingsForm.cs.
- [X] T011 [US1] Wire the toolbar Settings button to resolve SettingsForm via DI, apply current values, and log operator context inside D:\Documents\Code Project\GOTF-laser-tag\Ui\StatusForm.cs.
- [X] T012 [US1] Remove the match-length preflight validation and update explanatory messaging inside D:\Documents\Code Project\GOTF-laser-tag\Ui\StatusForm.cs and D:\Documents\Code Project\GOTF-laser-tag\Services\MatchCoordinator.cs so only team/player checks remain.
- [X] T013 [US1] Emit structured logging for settings loads, validations, saves, and restart prompts in D:\Documents\Code Project\GOTF-laser-tag\Services\SettingsPersistenceService.cs and D:\Documents\Code Project\GOTF-laser-tag\appsettings.json (logging categories).

**Checkpoint**: Toolbar and Settings form operate independently with validation, persistence, and preflight regression resolved.

---

## Phase 4: User Story 2 - Real-Time Relay Monitor (Priority: P2)

**Goal**: Provide a Relay Monitor window that renders the latest combined payload in a fixed JSON layout, updating fields live and highlighting stale data.

**Independent Test**: Trigger /match and /prop events, open the Relay Monitor from the toolbar, and verify the JSON fields refresh within one second while stale data warnings appear when updates pause for more than five seconds.

### Implementation for User Story 2

- [X] T014 [US2] Extend the relay snapshot data contracts with display metadata inside D:\Documents\Code Project\GOTF-laser-tag\Domain\RelaySnapshot.cs to back the monitor UI.
- [X] T015 [US2] Publish snapshot-changed events and timestamps from D:\Documents\Code Project\GOTF-laser-tag\Services\MatchCoordinator.cs into RelaySnapshotCache for UI consumption.
- [X] T016 [US2] Build the Relay Monitor form with split Match/Prop JSON viewers, last-updated timestamp, and stale indicators inside D:\Documents\Code Project\GOTF-laser-tag\Ui\RelayMonitorForm.cs.
- [X] T017 [US2] Connect the toolbar Relay Monitor button to launch RelayMonitorForm via ToolbarNavigationService and auto-refresh from RelaySnapshotCache in D:\Documents\Code Project\GOTF-laser-tag\Ui\StatusForm.cs.
- [X] T018 [US2] Add diagnostics and operator-facing copy describing relay state, stale thresholds, and disabled relay scenarios in D:\Documents\Code Project\GOTF-laser-tag\Services\RelaySnapshotCache.cs and D:\Documents\Code Project\GOTF-laser-tag\Ui\RelayMonitorForm.cs.

**Checkpoint**: Relay Monitor surfaces accurate, timely payload data independent of Settings or Debug panels.

---

## Phase 5: User Story 3 - Debug Payload Injector (Priority: P3)

**Goal**: Provide a Debugging panel that lets operators craft match/prop/combined payloads, validates JSON, and pushes them through the existing relay pipeline with clear success/failure reporting.

**Independent Test**: From the toolbar, open Debugging, craft a Combined payload, send it, and observe successful downstream relay logs without needing other utilities.

### Implementation for User Story 3

- [X] T019 [US3] Add DebugPayloadTemplate models with payload type metadata and last-result fields in D:\Documents\Code Project\GOTF-laser-tag\Domain\DebugPayloadTemplate.cs.
- [X] T020 [US3] Implement a DebugPayloadService that validates JSON, serializes to existing DTOs, and calls IRelayService with shared auth headers inside D:\Documents\Code Project\GOTF-laser-tag\Services\DebugPayloadService.cs.
- [X] T021 [US3] Create DebugPayloadForm with payload type selector, JSON editor, schema validation feedback, and send/cancel controls in D:\Documents\Code Project\GOTF-laser-tag\Ui\DebugPayloadForm.cs.
- [X] T022 [US3] Wire the toolbar Debug button to instantiate DebugPayloadForm via ToolbarNavigationService and disable toolbar buttons during in-flight submissions in D:\Documents\Code Project\GOTF-laser-tag\Ui\StatusForm.cs.
- [X] T023 [US3] Surface downstream HTTP status and errors to operators while logging each attempt in D:\Documents\Code Project\GOTF-laser-tag\Services\DebugPayloadService.cs and D:\Documents\Code Project\GOTF-laser-tag\logs.

**Checkpoint**: Debug payload injection works independently with guarded validation and telemetry.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, diagnostics, and follow-ups after all user stories function.

- [X] T024 Update operator guidance with toolbar, settings, monitor, and debug steps inside D:\Documents\Code Project\GOTF-laser-tag\specs\001-toolbar-utilities\quickstart.md.
- [X] T025 Refresh design references (plan.md/research) to capture restart prompts, relay monitor cadence, and debug pipeline details in D:\Documents\Code Project\GOTF-laser-tag\specs\001-toolbar-utilities\plan.md.
- [X] T026 Add release notes or diagnostics coverage for removed match-length check and new logging to D:\Documents\Code Project\GOTF-laser-tag\agents.md or the repo CHANGELOG if present.
- [X] T027 Record a reminder to revisit the pending latency issue and proposed mitigation ideas in D:\Documents\Code Project\GOTF-laser-tag\specs\001-toolbar-utilities\plan.md.

---

## Dependencies & Execution Order

- **Setup (Phase 1)** -> complete before modifying source so AGENTS requirements remain aligned.
- **Foundational (Phase 2)** -> depends on Setup; establishes DI, navigation, and snapshot cache required for all later work.
- **User Story 1 (Phase 3)** -> depends on Foundational; delivers toolbar shell and Settings experience (MVP).
- **User Story 2 (Phase 4)** -> depends on Foundational; may start after Phase 2 but benefits from toolbar scaffolding from US1 for button wiring.
- **User Story 3 (Phase 5)** -> depends on Foundational; optional dependency on US1 for toolbar button wiring patterns.
- **Polish (Phase 6)** -> runs last after desired user stories ship.

### Story Completion Order

1. User Story 1 (P1, MVP blocker)
2. User Story 2 (P2, builds on toolbar shell)
3. User Story 3 (P3, adds debugging capability)

---

## Parallel Execution Examples

### User Story 1

- Run T008 (view-model creation) while another developer tackles T007 (toolbar shell) since they touch different files.
- After T008 completes, T010 (SettingsForm UI) and T009 (persistence service) can proceed in parallel and converge before wiring tasks T011-T013.

### User Story 2

- Execute T014 (data contract) and T016 (form scaffolding) concurrently while T015 focuses on MatchCoordinator events; merge before toolbar wiring T017.

### User Story 3

- T019 (models) and T020 (service) can run in parallel, enabling T021 (UI) to start once T019 finalizes, followed by T022-T023 for integration/logging.

---

## Implementation Strategy

- **MVP (User Story 1)**: Finish Setup, Foundational, and US1 tasks to deliver toolbar + Settings + preflight fix before tackling other utilities.
- **Incremental Delivery**: After MVP validation, add Relay Monitor (US2) and Debug Injector (US3) sequentially or in parallel depending on bandwidth; each story should be testable independently per acceptance criteria.
- **Governance**: At every phase, ensure new code carries explanatory comments, respects constitutional relay buffering rules, and reuses existing services (MatchCoordinator, RelayService, Focus automation) as mandated in AGENTS.md.
