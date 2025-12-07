# Implementation Plan: Toolbar Utilities Enhancements

**Branch**: 001-toolbar-utilities | **Date**: 2025-12-07 | **Spec**: [/specs/001-toolbar-utilities/spec.md](./spec.md)
**Input**: Feature specification from /specs/001-toolbar-utilities/spec.md

**Note**: This template is filled in by the /speckit.plan command. See .specify/templates/commands/plan.md for the execution workflow.

## Summary

Add an always-on toolbar to the WinForms status window that launches Settings, Relay Monitor, and Debugging utilities. Settings now surfaces every appsettings.json option with inline validation plus restart prompts, Relay Monitor renders the latest combined payload with freshness/stale indicators, and Debugging injects operator-crafted payloads (Match, Prop, or Combined) through the existing relay pipeline with downstream status reporting. The preflight match-length validation is removed because the host no longer supplies duration before countdown.

## Technical Context

**Language/Version**: C# / .NET 9.0 (net9.0-windows10.0.19041.0) WinForms  
**Primary Dependencies**: WinForms UI stack, Microsoft.Extensions.Hosting/Options, System.Text.Json, Serilog, existing MatchCoordinator & RelayService  
**Storage**: JSON configuration (appsettings.json) plus in-memory snapshots/buffers (MatchCoordinator state)  
**Testing**: xUnit + Microsoft.NET.Test.Sdk (Tests project)  
**Target Platform**: Windows 10+ tray utility (always-on-top status form, single-file win-x64)  
**Project Type**: Single desktop/tray application (Application.csproj)  
**Performance Goals**: Relay Monitor reflects new match/prop fields within 1 second; toolbar actions open target forms within 500 ms; debug payload submissions complete within 2 seconds when relay reachable  
**Constraints**: Must honor Constitution principles (relay buffering accuracy, ICE focus automation, code commentary), remove match-length preflight validation without regressing others, maintain always-on-top accessibility, smart binding, CIDR/bearer enforcement, and retain clear operator-facing tooltips/logging for each toolbar utility  
**Scale/Scope**: Single coordinator instance, one operator per workstation, dozens of toolbar-driven interactions per match session

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- AGENTS.md reviewed; any new or conflicting scope documented there before work proceeds.
- Plans respect platform constraints: .NET 9 Windows tray app, always-on-top status window, single-file self-contained win-x64 output.
- Feature design aligns with /prop and /match contracts, FSM timing (180s no-plant auto-end, 40s defuse window), and winner override rules.
- Validation approach covers team/player name patterns from appsettings.json and enforcement behaviors (including optional match cancellation).
- Relay buffering, downstream payload completeness, and focus automation (ICE + Ctrl+S) impacts are captured with testability notes.
- Security assumptions include smart binding to active interfaces plus Bearer token and CIDR allowlist requirements.
- Release and testing plans preserve tray/UI visibility and do not regress packaging or appsettings defaults unless explicitly amended.
- Implementation notes document how each touched area of code will receive or retain explanatory comments describing what the section does.

## Project Structure

### Documentation (this feature)

```
specs/001-toolbar-utilities/
?? plan.md          # /speckit.plan output (this file)
?? research.md      # Phase 0 research decisions
?? data-model.md    # Phase 1 entity/state references
?? quickstart.md    # Phase 1 operator/testing guide
?? contracts/       # Phase 1 contracts
?? tasks.md         # Phase 2 (/speckit.tasks)
```

### Source Code (repository root)

```
/
?? Application.csproj      # WinForms tray app entry
?? Program.cs              # Host + WinForms startup
?? Domain/                 # DTOs, options, enums
?? Services/               # MatchCoordinator, RelayService, config/debug services
?? Ui/                     # StatusForm, MatchResultForm, toolbar utilities
?? Http/                   # Middleware + endpoints
?? Interop/                # Win32 helpers
?? Tests/                  # xUnit suites
?? specs/                  # Feature specs/plans
```

**Structure Decision**: Maintain a single WinForms tray application rooted at Application.csproj. UI additions live in Ui/, supporting DTOs/options in Domain/, service logic (config persistence, relay snapshot feeds, debug injector) in Services/, middleware remains in Http/, and regression tests remain under Tests/. Documentation stays scoped under specs/001-toolbar-utilities/.

## Release Notes & Diagnostics

- Toolbar now includes Settings, Relay Monitor, and Debug buttons with Alt+S/Alt+R/Alt+D shortcuts; all actions log to Diagnostics with operator context.
- Settings form persists every appsettings.json section, enforces option validation, and emits structured ?SettingsSaved? entries (sections that require restart are listed per save).
- Relay Monitor warns when no payload arrives for >5 seconds and clearly indicates when relay is disabled while still showing the cached payload.
- Debug Payload Injector validates JSON, merges partial payloads with the latest cached state, and surfaces downstream HTTP status for each attempt; failures/logs capture the payload type and error message.
- Preflight UI no longer blocks on match length, replacing the warning with informational guidance that duration will appear after countdown begins.

## Follow-ups

- Latency instrumentation: collect operator feedback on whether the Relay Monitor stale threshold (5s) should be tunable; revisit after measuring real-world cadence drift.
- Outstanding action: revisit the deferred relay latency issue (logged earlier) once field data is available to determine whether additional buffering, telemetry, or configuration knobs are required.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|--------------------------------------|
| N/A | | |
