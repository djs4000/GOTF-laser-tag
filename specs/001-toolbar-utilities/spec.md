# Feature Specification: Toolbar Utilities Enhancements

**Feature Branch**: `001-toolbar-utilities`  
**Created**: 2025-12-06  
**Status**: Draft  
**Input**: User description: "add toolbar to the main form. Add options for settings, relay monitor and debugging. Add a settings form that should allow configuration of everything in the appsettings.json. The relay monitor should bring up a form that shows a realtime view of the data being sent via relay. Ideally, not as scorlling text but maybe a fixed json format what updates the valuse in realtime. Debugging should include options to send specific specific data to the downstream server. Lastly, as a small correction, remove the match length pre-flight check as the laser tag software does not send that info before the game starts and therefor we cannot check before the match begins."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Toolbar Access to Configuration (Priority: P1)

Operators want a single toolbar on the always-on-top status window that exposes Settings, Relay Monitor, and Debugging launchers so they can quickly reach each panel during matches without digging through menus. Once inside Settings they must be able to review and edit every option from `appsettings.json` with inline validation, save changes, and confirm that the application acknowledges the update.

**Why this priority**: Without centralized navigation and editable configuration, field staff waste time alt-tabbing and hand-editing JSON, which risks downtime before a match.

**Independent Test**: Launch the application, adjust Relay options via the toolbar-accessed Settings form, save, and verify the updated configuration is persisted and reflected in the UI without using other stories.

**Acceptance Scenarios**:

1. **Given** the app is running, **When** the operator clicks the new Settings toolbar button, **Then** a modal form opens showing every configuration section populated with current values and contextual validation feedback.
2. **Given** the operator edits Match.AutoEndNoPlantAtSec and saves, **When** they reopen the form, **Then** the new value is displayed and persisted to the configuration store without requiring manual JSON edits.
3. **Given** the preflight page previously displayed a match-length check, **When** the operator opens preflight before a host provides match duration, **Then** no blocking error appears because the match length preflight check has been removed.

---

### User Story 2 - Real-Time Relay Monitor (Priority: P2)

Coordinators need to confirm that the downstream Relay receives accurate combined payloads in real time. A dedicated Relay Monitor window must visualize the latest Match + Prop payload in a fixed JSON layout that updates field values in place instead of scrolling logs.

**Why this priority**: Visibility into the outbound payload reduces troubleshooting time when downstream automation misbehaves.

**Independent Test**: Trigger simulated match + prop events, open the Relay Monitor, and verify that each field updates within one refresh cycle even without using Settings or Debugging.

**Acceptance Scenarios**:

1. **Given** the relay is enabled, **When** a new prop status arrives, **Then** the Relay Monitor window automatically refreshes the Prop section fields to the latest values without adding duplicate lines.
2. **Given** no relay traffic has been sent for several seconds, **When** the operator views the monitor, **Then** the UI clearly indicates the timestamp of the last payload so they can spot stale data.

---

### User Story 3 - Debug Payload Injector (Priority: P3)

Support engineers require a way to send curated test payloads to the downstream system without waiting for live hardware. A Debugging panel should allow them to choose a payload type (prop, match, or combined), edit the JSON content safely, and submit it through the existing relay pipeline with clear confirmation.

**Why this priority**: Controlled payload injection accelerates diagnostics and reduces the need to spoof devices externally.

**Independent Test**: Open the Debugging panel via the toolbar, craft a match payload with custom timestamps, send it, and confirm the downstream endpoint logs the injected data even if Relay Monitor or Settings remain untouched.

**Acceptance Scenarios**:

1. **Given** the operator selects "Combined Payload" and populates JSON, **When** they press Send, **Then** the coordinator transmits the payload to the downstream relay endpoint and shows confirmation or errors.
2. **Given** the operator provides malformed JSON, **When** they attempt to send, **Then** the panel highlights the problem and blocks transmission until the payload is corrected.

---

### Edge Cases

- What happens when the operator edits a configuration value to something outside allowed ranges (e.g., negative timer durations)?
- How does the Relay Monitor behave if the relay feature is disabled or unreachable?
- What happens if the operator attempts to send debug payloads while another debug transmission is in-flight or the downstream endpoint rejects the request?
- How does the UI respond when the configuration file is read-only or saving fails mid-operation?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The main application window MUST include a toolbar that is always visible and contains buttons for Settings, Relay Monitor, and Debugging; buttons must be reachable via mouse and keyboard shortcuts to preserve accessibility.
- **FR-002**: The Settings toolbar button MUST open a dedicated form that surfaces every section and property from `appsettings.json`, grouping related values (Http, Relay, Match, Preflight, UiAutomation, Diagnostics, etc.) with descriptive labels and helper text.
- **FR-003**: The Settings form MUST load current configuration values on open, validate inputs inline using the same constraints enforced by runtime options, and prevent saving when a value would violate AGENTS.md requirements (e.g., invalid CIDR or timer length).
- **FR-004**: Saving from the Settings form MUST persist changes to the configuration store used at runtime, show success/failure feedback, and apply updates immediately whenever the underlying subsystem supports hot reload. For sections that cannot be reloaded safely (e.g., HTTP binding changes), the UI must clearly prompt for a targeted restart requirement rather than forcing a blanket restart.
- **FR-005**: Operators MUST be able to cancel edits or reset a section to its last persisted value without affecting other sections.
- **FR-006**: The Preflight panel MUST stop performing the match length check prior to the host supplying duration data, removing any blocking warnings tied to that check while keeping team/player validations intact.
- **FR-007**: The Relay Monitor MUST present the latest combined payload in a stable JSON layout divided into Match and Prop panels, updating individual value fields whenever new data arrives and indicating the timestamp of the last payload.
- **FR-008**: Relay Monitor updates MUST occur without creating additional log lines; the UI should highlight stale data (e.g., if no payload arrives within the configured relay cadence) so operators can identify lapsed communication.
- **FR-009**: The Debugging panel MUST allow operators to select a payload type (Match, Prop, Combined), edit JSON content with validation, and submit it through the downstream relay pipeline using the same authentication/headers as standard relays.
- **FR-010**: Debug submissions MUST provide a visible success/failed status, include error messaging when downstream responses are non-2xx, and log the attempt via existing diagnostics.
- **FR-011**: All new UI surfaces MUST include explanatory text or tooltips so operators understand what each section controls, fulfilling the Code Commentary Transparency principle for user-facing interactions.
- **FR-012**: Application logging MUST note when settings are changed, relay monitor is opened, or debug payloads are sent, including operator context when available, to aid post-incident reviews.

### Key Entities *(include if feature involves data)*

- **Application Settings Profile**: Represents the structured values sourced from `appsettings.json`, including Http binding, Relay configuration, Match timing thresholds, Preflight validation rules, UI automation options, and Diagnostics controls; must capture both current persisted values and pending edits.
- **Relay Snapshot**: Represents the most recent combined payload dispatched downstream, containing MatchSnapshotDto data (status, remaining time, winner team, player data) and PropStatusDto data (state, timestamps); used to drive the Relay Monitor UI.
- **Debug Payload Template**: Represents a user-crafted JSON body tied to a payload type plus metadata (e.g., friendly name, last sent time, downstream response code) so repeated diagnostic tests can be executed consistently.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Operators can modify and save 100% of `appsettings.json` values through the Settings form within two minutes, with validation preventing invalid entries before persistence.
- **SC-002**: Relay Monitor updates reflect new prop or match data within one second of receipt and clearly flag when no payload has arrived for more than five seconds.
- **SC-003**: At least 90% of debug payload submissions succeed on the first attempt when the downstream endpoint is reachable, and any failure surfaces actionable error messaging without inspecting logs.
- **SC-004**: Preflight preparation produces zero false blocking errors about match length when the host has not yet provided a duration, ensuring operators can proceed to countdown states.
- **SC-005**: Field testers report a 50% reduction in time spent switching windows or editing JSON files during setup compared to the previous release, measured through user study or support feedback.

## Assumptions

- Configuration edits made through the Settings form are persisted to the same location and format currently used by the application (no alternate storage is introduced).
- Relay Monitor and Debugging panels rely on existing relay authentication/token configuration; no new credential prompts are required beyond what already exists.
- Real-time updates use existing state buffering within the coordinator, so no additional network subscriptions are necessary.

## Clarifications

### Session 2025-12-07

- Q: How should the application handle settings changes that might require a restart? → A: Apply edits live, but prompt for restart only for sections that cannot hot-reload safely (e.g., HTTP bindings).
