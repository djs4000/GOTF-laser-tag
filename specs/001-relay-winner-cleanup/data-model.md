# Data Model: Relay Winner Cleanup

**Branch**: 001-relay-winner-cleanup  
**Date**: 2025-12-06  
**Source Spec**: specs/001-relay-winner-cleanup/spec.md

## Entities

### CombinedPayload
- match: MatchSnapshot (required; latest buffered)
- prop: PropStatus (required; latest buffered)
- winner_reason: string? (HostTeamWipe | TeamElimination | ObjectiveDetonated | ObjectiveDefused | TimeExpiration)
- timestamp: number (source event timestamp for the triggering update)

### MatchSnapshot
- id: string
- status: string (e.g., Running, Completed, WaitingOnStart)
- remaining_time_ms: number?
- winner_team: string? (supplied by host, nullable)
- is_last_send: bool
- players: array<object>? (as provided by host)

Validation:
- winner_team accepted only when status == Completed and before objective override; otherwise derived at relay.

### PropStatus
- state: string (armed | planted | defusing | defused | exploded)
- timestamp: number
- uptime_ms: number

Validation:
- Ignore prop events during host countdown states.
- Overtime window: defuse allowed until plant_time + 40s when plant occurs at ≥180s.

### WinnerReason
- authority: HostTeamWipe | TeamElimination | ObjectiveDetonated | ObjectiveDefused | TimeExpiration
- source_detail: string (optional debug note such as "host winner before objective" or "no plant at 180s")

## Relationships
- CombinedPayload embeds MatchSnapshot and PropStatus; both must be present on every relay emission.
- Winner fields are resolved using both MatchSnapshot and PropStatus plus FSM timing rules.

## State Transitions (Prop)
- Idle → Armed → Planted → Defusing → Defused/Exploded → End
- Armed → End when elapsed >= 180s and no plant
- Planted/Defusing → End when elapsed >= plant_time + 40s

## Timing Rules
- No plant by 180s elapsed → defenders win (TimeExpiration).
- Plant at or after 180s → 40s defuse window; resolution by defuse/explode.
- Host team wipe before objective resolution → host winner honored.
