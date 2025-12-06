<!--
Sync Impact Report
- Version change: 1.0.0 -> 1.1.0
- Modified principles: (new) VI. Code Commentary Transparency
- Added sections: none
- Removed sections: none
- Templates requiring updates: .specify/templates/plan-template.md UPDATED; .specify/templates/spec-template.md REVIEWED (no change); .specify/templates/tasks-template.md UPDATED; .specify/templates/commands/ PENDING (directory absent)
- Follow-up TODOs: TODO(RATIFICATION_DATE) requires historical confirmation
-->
# Laser Tag Defusal Mode Orchestrator Constitution

## Core Principles

### I. AGENTS Alignment & Documentation-First
AGENTS.md is the immutable source of truth. No code or spec changes proceed without cross-referencing it. Any new or conflicting functionality requires user clarification and an AGENTS.md update before or alongside implementation.

### II. Platform & Packaging Integrity
The app targets .NET 9 on Windows as a tray application with an always-on-top status window. Distribution MUST be a single-file, self-contained win-x64 executable that preserves tray behavior and UI visibility.

### III. Preflight Validation Enforcement
Team names and player names MUST be validated against appsettings.json expectations. Enforcement of failures (including optional match cancellation) is mandatory when enabled. Match duration checks apply only in WaitingOnStart due to host timer limits.

### IV. Game State & Timing Correctness
Defusal FSM timing is authoritative: auto-end at 180s with no plant; allow 40s overtime for planted states; end immediately on defuse or explosion; ignore prop events during host countdown. State transitions and clocks must stay consistent with the defined FSM.

### V. Relay, Override, and Focus Reliability
Relay payloads must always include the latest match and prop data buffered from both sources. Winner override rules apply (host team wipe, objective events, or time expiration). Focus automation must target the ICE window and send Ctrl+S when objective outcomes require immediate host termination.

### VI. Code Commentary Transparency
Every code contribution MUST include descriptive comments that explain what each section, class, and complex block of logic does. Comments must precede or sit atop the relevant code, stay synchronized with behavior, and be substantial enough that new contributors understand intent without reverse-engineering.

## HTTP & Security Constraints

Inbound POST endpoints: /prop (armed, planted, defusing, defused, exploded) and /match (status, clock, optional players). Smart binding must only attach to interfaces marked Up. Authentication uses optional Bearer tokens and CIDR allowlists; invalid tokens or denied IPs return 401.

## Development Workflow & Quality Gates

Plans, specs, and tasks must validate against AGENTS.md and this constitution before execution. Testing and UI work must honor preflight indicators, status window visibility, relay buffering, and winner override logic. Code reviews block merges unless the touched files contain up-to-date explanatory comments for each logical section. Release artifacts must remain single-file win-x64 builds aligned with appsettings defaults unless amended.

## Governance

This constitution supersedes other practices for governance. Amendments require concurrent updates to AGENTS.md and relevant templates, explicit version bumps, and documented rationale. Versioning follows semantic rules (MAJOR for principle changes/removals, MINOR for additions/expansions, PATCH for clarifications). Compliance reviews must confirm adherence to platform constraints, validation rules, FSM timing, relay overrides, and packaging before merges or releases.

**Version**: 1.1.0 | **Ratified**: TODO(RATIFICATION_DATE) | **Last Amended**: 2025-12-06
