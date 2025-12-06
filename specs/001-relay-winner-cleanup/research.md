# Research: Relay Winner Cleanup

**Branch**: 001-relay-winner-cleanup  
**Date**: 2025-12-06  
**Spec**: specs/001-relay-winner-cleanup/spec.md

## Decision 1: Single Combined Relay Endpoint
- **Decision**: Remove the legacy dual relay endpoints and keep only the combined payload publisher in Services/RelayService, ensuring every dispatch uses that path.
- **Rationale**: Downstream consumers and AGENTS.md expect exactly one outbound endpoint carrying both Match + Prop; eliminating extras/ reduces drift, simplifies configuration, and prevents null payloads caused by mismatched handlers.
- **Alternatives Considered**: (a) Keep old endpoints hidden behind a config flag�rejected because it violates the architecture and prolongs dead code; (b) Proxy legacy endpoints to the combined relay�adds needless indirection and doubles HTTP events.

## Decision 2: Winner Authority Precedence
- **Decision**: Apply host team-wipe winners only when reported before prop resolution; otherwise prefer prop detonation/defuse outcomes, and fall back to TimeAuthority when no plant occurs by 180 seconds (defenders win).
- **Rationale**: Mirrors AGENTS.md sections �Host Authority,� �Objective Authority,� and �Time Authority,� delivering rule 10.2.5�10.2.7 compliance and explaining the order of operations.
- **Alternatives Considered**: (a) Always trust host WinnerTeam�even post detonation�which would ignore objective rules; (b) Always override host WinnerTeam�ignores legitimate elimination wins and causes disputes.

## Decision 3: Buffered Payload Consistency
- **Decision**: Maintain separate latest-match and latest-prop snapshots inside MatchCoordinator; whenever either changes, dispatch RelayService with both structures (deep copies) so payloads never contain null components.
- **Rationale**: AGENTS.md mandates buffering and cadence handling; deep copies prevent subsequent mutations before serialization and guarantee deterministic JSON for downstream monitors.
- **Alternatives Considered**: (a) Emit only the changed component�breaks downstream parsing; (b) Wait for both sources every time�introduces latency and can hang when one source pauses.

## Decision 4: Deterministic Logging & Commentary
- **Decision**: Extend diagnostics and inline comments around winner decisions, buffer updates, and ICE focus triggers, documenting every branch of the precedence logic per the Code Commentary Transparency principle.
- **Rationale**: Operators and future maintainers need to understand why a team won directly from logs/comments without reverse engineering; this also aids MatchCoordinatorTests.
- **Alternatives Considered**: (a) Rely on current sparse logs�insufficient for auditing; (b) Move documentation outside code�risks drift and violates commentary mandate.
