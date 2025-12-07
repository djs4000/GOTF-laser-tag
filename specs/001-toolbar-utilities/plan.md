# Implementation Plan: Toolbar Utilities Enhancements

**Branch**: `001-toolbar-utilities` | **Date**: 2025-12-07 | **Spec**: [specs/001-toolbar-utilities/spec.md](./spec.md)
**Input**: Feature specification from `/specs/001-toolbar-utilities/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/commands/plan.md` for the execution workflow.

## Summary

Add an always-on toolbar to the WinForms status window so operators can reach Settings, Relay Monitor, and Debug utilities instantly. Settings must expose the full `appsettings.json` surface with inline validation and restart prompts. Relay Monitor renders the combined payload in a fixed JSON layout fed by cached snapshots, while the Debug injector validates crafted payloads before relaying them downstream. The plan keeps the tray app on .NET 9, honors FSM/relay rules, and removes the obsolete match-length preflight check.

## Technical Context

**Language/Version**: C# / .NET 9.0 (net9.0-windows10.0.19041.0) WinForms  
**Primary Dependencies**: Microsoft.Extensions.Hosting, Microsoft.Extensions.Options, System.Text.Json, Serilog, existing MatchCoordinator & RelayService, WinForms controls (ToolStrip, TableLayoutPanel)  
**Storage**: JSON configuration file (`appsettings.json`) and in-memory buffers for relay snapshots/debug templates  
**Testing**: xUnit + Microsoft.NET.Test.Sdk + manual UI validation from quickstart  
**Target Platform**: Windows 10+ tray application (always-on-top window)  
**Project Type**: Single desktop/tray application (`Application.csproj`)  
**Performance Goals**: Toolbar actions open their forms within 500?ms; Relay Monitor refreshes payload data within 1?s; Debug submissions complete within 2?s or report an error; Settings saves in <1?s  
**Constraints**: Must respect AGENTS/Constitution rules (single-file win-x64 publish, ICE focus automation, FSM timing, relay buffering completeness, code commentary). Toolbar buttons must stay keyboard accessible; UI must remain on top.  
**Scale/Scope**: Single coordinator instance per workstation; dozens of toolbar interactions per match; payload cadence ~100?ms (match) / ~500?ms (prop); expected 2 operators max per site.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- AGENTS.md reviewed; toolbar utilities operate within documented architecture and any deviations require AGENTS updates before coding. ?
- Platform constraints satisfied: WinForms on .NET 9, tray window remains always-on-top, publish remains single-file win-x64. ?
- Feature design aligns with /prop and /match contracts, FSM timings (180?s no-plant, 40?s overtime) and winner override rules—relay monitor + debug reuse same DTOs. ?
- Validation covers team/player name patterns with optional match cancellation; removal of match-length check respects WaitingOnStart limitation. ?
- Relay buffering + downstream completeness maintained via RelaySnapshot cache feeding monitor and debug injector, ICE focus automation unaffected. ?
- Security assumptions (smart binding, Bearer token, CIDR filters) preserved; toolbar surfaces do not bypass auth. ?
- Release/test plans maintain visible status window and existing packaging/appsettings defaults. ?
- Implementation requires descriptive comments for each new section (Code Commentary Transparency) and plan documents where comments must be refreshed. ?

## Project Structure

### Documentation (this feature)

```text
specs/001-toolbar-utilities/
+- plan.md              # /speckit.plan output (this file)
+- research.md          # Phase 0 design decisions
+- data-model.md        # Phase 1 entity/state references
+- quickstart.md        # Operator/test workflow
+- contracts/           # API/data contract references for toolbar utilities
+- tasks.md             # Generated later by /speckit.tasks
```

### Source Code (repository root)

```text
Application.csproj          # WinForms tray app entry
Program.cs                  # Host + WinForms startup, DI
Domain/
+- ApplicationSettingsProfileViewModel.cs
+- DebugPayloadTemplate.cs
+- RelaySnapshot*.cs
Services/
+- MatchCoordinator.cs
+- RelayService.cs / IRelayService.cs
+- RelaySnapshotCache.cs
+- SettingsPersistenceService.cs
+- DebugPayloadService.cs
+- ToolbarNavigationService.cs
Ui/
+- StatusForm.cs (toolbar shell, preflight panel)
+- SettingsForm.cs
+- RelayMonitorForm.cs
+- DebugPayloadForm.cs
Http/                       # Existing endpoints (/prop, /match)
Interop/                    # Native automation helpers
Tests/                      # xUnit suites
specs/                      # Feature specs/plans
```

**Structure Decision**: Maintain single WinForms application rooted at `Application.csproj`. Documentation stays under `specs/001-toolbar-utilities`. Domain/Services/Ui folders already host the components this feature extends; no additional projects introduced.

## Post-Design Constitution Check

After drafting research, data model, contracts, and quickstart, the above constitution requirements remain satisfied. No new violations or exceptions introduced; tooling changes respect AGENTS documentation-first rules.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|--------------------------------------|
| _None_ | | |
