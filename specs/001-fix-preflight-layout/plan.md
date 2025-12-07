# Implementation Plan: Status Form Layout Corrections

**Branch**: `001-fix-preflight-layout` | **Date**: 2025-12-07 | **Spec**: [specs/001-fix-preflight-layout/spec.md](./spec.md)
**Input**: Feature specification from `/specs/001-fix-preflight-layout/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/commands/plan.md` for the execution workflow.

## Summary

Fix the StatusForm layout so the Match, Prop, and Game Configuration/Pre-flight panels remain fully visible inside the default 1280×720 window and respond gracefully to resizing/DPI changes. Work centers on WinForms layout containers (TableLayoutPanel, FlowLayoutPanel, docking), padding, and minimum sizes so operators no longer see cropped preflight fields like in the provided screenshot.

The refreshed layout locks Match/Prop columns to 45% widths, keeps the Game Configuration column auto-sized with a 220px minimum, and automatically stacks that column once client width drops below ~1100px so no content clips.

## Technical Context

**Language/Version**: C# / .NET 9.0 WinForms (net9.0-windows10.0.19041.0)  
**Primary Dependencies**: WinForms layout controls (TableLayoutPanel, FlowLayoutPanel, SplitContainer), existing `StatusForm`, `ToolbarNavigationService`, `MatchCoordinator` data bindings  
**Storage**: N/A (uses existing in-memory coordinator state)  
**Testing**: Manual UI regression per quickstart; optional automated UI smoke via WinForms integration tests (not required)  
**Target Platform**: Windows 10+ tray application (always-on-top)  
**Project Type**: Single desktop/tray app (`Application.csproj`)  
**Performance Goals**: Layout adjustments must not regress startup (<2?s) and must keep resize redraws under 100?ms (no lag during drag).  
**Constraints**: Respect AGENTS constitution—single-file win-x64 packaging, ICE focus automation, relay buffer integrity, and Code Commentary Transparency (document layout sections).  
**Scale/Scope**: One StatusForm window; resolution targets 1280×720 default, responsive down to ~1024px width, up to ultrawide.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- AGENTS alignment confirmed: StatusForm already mandated; layout tweaks remain in-scope. ?
- Platform constraints maintained: still .NET 9 WinForms tray window with always-on-top behavior and single-file publish. ?
- FSM/relay logic untouched; layout only displays existing data, so /prop, /match contracts remain compliant. ?
- Validation UI retains team/player checks; removing match-length block already covered. ?
- Relay buffering and ICE focus automation unaffected; no new automation tasks introduced. ?
- Smart binding/Bearer/CIDR security untouched. ?
- Packaging/testing remains per constitution; manual UI docs will reflect layout steps. ?
- Plan notes to add/refresh explanatory comments for all layout sections. ?

## Project Structure

### Documentation (this feature)

```text
specs/001-fix-preflight-layout/
+- spec.md            # Feature requirements
+- plan.md            # This implementation plan
+- research.md        # Phase 0 findings
+- data-model.md      # Layout entities + constraints
+- quickstart.md      # Validation/playbook
+- contracts/         # (placeholder) no API deltas
+- tasks.md           # Generated later via /speckit.tasks
```

### Source Code (repository root)

```text
Application.csproj
Program.cs
Ui/
+- StatusForm.cs            # layout + toolbar shell
+- StatusForm.Designer.cs   # auto-generated layout declarations (if present)
Services/
+- ToolbarNavigationService.cs (only for reference)
+- MatchCoordinator.cs (data sources for UI bindings)
Domain/
+- PreflightOptions.cs / other DTOs (display data)
```

**Structure Decision**: Only `Ui/StatusForm.cs` (and possibly designer partial) require code changes. Documentation resides under the new spec folder; no additional projects or services are added.

## Complexity Tracking

_No constitution violations introduced; table remains empty._
