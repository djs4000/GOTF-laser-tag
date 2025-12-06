# Implementation Plan: Relay Winner Cleanup

**Branch**: `001-relay-winner-cleanup` | **Date**: 2025-12-06 | **Spec**: specs/001-relay-winner-cleanup/spec.md
**Input**: Feature specification from `/specs/001-relay-winner-cleanup/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/commands/plan.md` for the execution workflow.

## Summary

Remove legacy dual relay endpoints in favor of the combined payload relay, enforce buffered match+prop payload completeness, preserve focus automation (Ctrl+S to ICE) on objective outcomes, and correct winner determination to respect host team wipes, objective outcomes, and time expiration per AGENTS.md while surfacing winner_reason for auditing.

## Technical Context

**Language/Version**: C# / .NET 9 (win-x64)  
**Primary Dependencies**: WinForms UI; HttpListener-style HTTP handling; Microsoft.Extensions.Configuration/Logging  
**Storage**: In-memory buffering only  
**Testing**: xUnit test project (Tests/Tests.csproj)  
**Target Platform**: Windows tray app, always-on-top status window  
**Project Type**: Single desktop application + tests  
**Performance Goals**: Sub-second relay updates; honor host (~100ms) and prop (~500ms) cadences without null payload components  
**Constraints**: Single-file self-contained win-x64; smart binding only to Up interfaces; focus automation targets ICE window with Ctrl+S and must be regression-tested  
**Scale/Scope**: Small desktop app; two inbound endpoints (/prop, /match) and one combined relay

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- AGENTS.md reviewed; any new or conflicting scope documented there before work proceeds.
- Plans respect platform constraints: .NET 9 Windows tray app, always-on-top status window, single-file self-contained win-x64 output.
- Feature design aligns with /prop and /match contracts, FSM timing (180s no-plant auto-end, 40s defuse window), and winner override rules.
- Validation approach covers team/player name patterns from appsettings.json and enforcement behaviors (including optional match cancellation).
- Relay buffering, downstream payload completeness, and focus automation (ICE + Ctrl+S) impacts are captured with testability notes.
- Security assumptions include smart binding to active interfaces plus Bearer token and CIDR allowlist requirements.
- Release and testing plans preserve tray/UI visibility and do not regress packaging or appsettings defaults unless explicitly amended.

## Project Structure

### Documentation (this feature)

```text
specs/001-relay-winner-cleanup/
?? plan.md          # This file (/speckit.plan output)
?? research.md      # Phase 0 output
?? data-model.md    # Phase 1 output
?? quickstart.md    # Phase 1 output
?? contracts/       # Phase 1 output
```

### Source Code (repository root)

```text
Domain/
Http/
Interop/
Services/
Ui/
Program.cs
Application.csproj

Tests/
?? MatchCoordinatorTests.cs
?? Tests.csproj
?? (bin/obj)
```

**Structure Decision**: Single desktop app with supporting Domain/Services/Http/Ui/Interop folders and a Tests project for xUnit coverage; feature work will touch Http and Services for relay/winner logic and may adjust Ui for status/result display.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|--------------------------------------|
| None | N/A | N/A |
