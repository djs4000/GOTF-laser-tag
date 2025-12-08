# Feature Specification: Relay Winner Cleanup

**Feature Branch**: `001-relay-winner-cleanup`  
**Created**: 2025-12-06  
**Status**: Draft  
**Input**: User description: "need to clean up the code base. there are several legacy features such as the two relay endpoints instead the new one combined payload. The end of match winner logic is still not quite right. It does not take in to account winning by elemination."

## User Scenarios & Testing *(mandatory)*

<!--
  IMPORTANT: User stories should be PRIORITIZED as user journeys ordered by importance.
  Each user story/journey must be INDEPENDENTLY TESTABLE - meaning if you implement just ONE of them,
  you should still have a viable MVP (Minimum Viable Product) that delivers value.
  
  Assign priorities (P1, P2, P3, etc.) to each story, where P1 is the most critical.
  Think of each story as a standalone slice of functionality that can be:
  - Developed independently
  - Tested independently
  - Deployed independently
  - Demonstrated to users independently
-->

### User Story 1 - Single Combined Relay (Priority: P1)

As a downstream consumer, I need the orchestrator to relay only the combined payload endpoint so I receive consistent match+prop data without legacy duplicate endpoints.

**Why this priority**: Removes ambiguity for downstream systems and enforces the architecture in AGENTS.md.

**Independent Test**: Post sample /prop and /match updates and verify only the combined relay is called with latest buffered match and prop objects.

**Acceptance Scenarios**:

1. **Given** combined relay is enabled, **When** /prop updates arrive without /match updates, **Then** relay payload still includes latest non-null /match snapshot with the new prop data.
2. **Given** legacy relay endpoints exist in code, **When** the app runs, **Then** those endpoints are not exposed or invoked and only the combined relay is used.

---

### User Story 2 - Correct Winner Determination (Priority: P1)

As a coordinator, I need end-of-match winner logic to honor host team elimination as a valid victory condition alongside prop outcomes and time rules.

**Why this priority**: Ensures match results align with rule 10.2.7 and AGENTS.md, preventing wrong winners.

**Independent Test**: Simulate host Completed status with winner due to elimination before prop resolution and verify relay uses host winner; simulate prop detonation/defuse overriding host winner; simulate timeout with no plant awarding defenders.

**Acceptance Scenarios**:

1. **Given** host reports Completed with WinnerTeam due to team wipe before detonation/defuse, **When** relay sends final payload, **Then** winner matches host WinnerTeam.
2. **Given** prop reports Exploded or Defused while match running, **When** relay sends final payload, **Then** winner is overridden per objective outcome and host winner is ignored.
3. **Given** match time expires with no plant, **When** final payload is sent, **Then** defenders are set as winner due to expiration.

---

### User Story 3 - Deterministic Relay Content (Priority: P2)

As a maintainer, I need relay payloads to consistently include both match and prop objects populated with the latest buffered state so downstream testing and auditing are reliable.

**Why this priority**: Avoids null/empty fields and makes validation deterministic across cadence mismatches.

**Independent Test**: Send alternating /match and /prop updates at different cadences and verify every relay call contains both objects fully populated with last-known values.

---

### Edge Cases

- Relay invoked when only one source has ever reported (must include that source and omit nulls by using last-known or initial defaults).
- Host reports Completed with WinnerTeam after prop Exploded/Defused (must ignore host winner and keep objective winner).
- Prop reports during host countdown states (must be ignored per FSM).
- Network/binding misconfiguration to inactive interfaces (smart binding skips them without failure).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Disable and remove legacy dual relay endpoints; only the combined payload relay is exposed and used.
- **FR-002**: Combined relay MUST always include both match and prop objects populated from latest buffered state, never sending null/empty components.
- **FR-003**: Winner calculation MUST ignore host-supplied winners; authority is derived solely from objective resolution (explode → attackers, defuse → defenders), time expiration (no plant by 180s → defenders), or team elimination.
- **FR-004**: Relay payload MUST include final winner and reason aligned to the applied authority (Objective, Time expiration, Team elimination) when determinable.
- **FR-005**: Prop events during host countdown MUST be ignored; FSM timing (auto-end at 180s no plant, 40s overtime for planted states) MUST remain unchanged.
- **FR-006**: Focus automation MUST still target the ICE window and issue Ctrl+S whenever objective outcomes (detonation or defusal) end the match so the host terminates correctly.

### Key Entities *(include if feature involves data)*

- **CombinedPayload**: Contains MatchSnapshot and PropStatus objects with winner_reason derived per authority; never null components.
- **MatchSnapshot**: Status, remaining_time_ms, winner_team (nullable until resolved), is_last_send, players (if provided).
- **PropStatus**: State (armed, planted, defusing, defused, exploded), timestamp, uptime_ms.
- **WinnerReason**: Authority source tag (TeamElimination, ObjectiveDetonated, ObjectiveDefused, TimeExpiration) for auditability.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of relay calls use the combined endpoint; no legacy relay invocations observed in telemetry during regression suite.
- **SC-002**: In simulated mixed-cadence updates, every relay payload contains both match and prop objects populated with last-known values (0 null components across 200 consecutive relays).
- **SC-003**: Winner outcomes match expected authority in all scripted scenarios (prop explode, prop defuse, no-plant timeout, team elimination) with 0 mismatches across test matrix.
- **SC-004**: Final payload includes winner_reason whenever an outcome is determinable in end-of-match relays, verified via automated tests or log inspection.
