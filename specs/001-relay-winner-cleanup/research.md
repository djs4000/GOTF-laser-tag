# Research: Relay Winner Cleanup

**Branch**: 001-relay-winner-cleanup  
**Date**: 2025-12-06  
**Spec**: specs/001-relay-winner-cleanup/spec.md

## Decisions

- **Relay consolidation**: Use only the combined payload relay; remove legacy dual endpoints.
  - **Rationale**: Matches AGENTS.md architecture; prevents split downstream handling and null component relays.
  - **Alternatives considered**: Keep legacy endpoints alongside combined relay (rejected: increases ambiguity and testing surface).

- **Winner authority**: Honor host team wipe when reported before objective resolution; otherwise override with objective outcome (explode→attackers, defuse→defenders) or time expiration (no plant by 180s→defenders).
  - **Rationale**: Aligns with rules 10.2.5/6/7 in AGENTS.md and avoids incorrect winners.
  - **Alternatives considered**: Always override host winner with objective (rejected: violates host authority for early team wipes).

- **Relay buffering completeness**: Every relay payload carries latest match and prop objects (no nulls), using buffered cadence handling.
  - **Rationale**: Downstream consumers require deterministic payloads despite differing inbound cadences.
  - **Alternatives considered**: Emit partial payloads when one source missing (rejected: violates buffering requirement).
