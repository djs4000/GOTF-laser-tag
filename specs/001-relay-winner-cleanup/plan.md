# Implementation Plan: Relay Winner Cleanup

**Branch**: `001-relay-winner-cleanup` | **Date**: 2025-12-06 | **Spec**: [/specs/001-relay-winner-cleanup/spec.md](./spec.md)
**Input**: Feature specification from `/specs/001-relay-winner-cleanup/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/commands/plan.md` for the execution workflow.

## Summary

Retire the legacy match-only/prop-only relay endpoints and rely exclusively on the combined payload relay while guaranteeing each dispatch carries the latest Match + Prop snapshots. Refine MatchCoordinator to enforce winner precedence (host elimination before objective resolution, objective outcomes overriding later host reports, defensive wins on time expiration) and keep buffered payloads deterministic so downstream systems always receive authoritative data with winner_reason context.

## Technical Context

**Language/Version**: C# / .NET 9.0 (net9.0-windows10.0.19041.0) WinForms tray app  
**Primary Dependencies**: WinForms UI stack, Microsoft.Extensions.Hosting & Options, System.Text.Json, ASP.NET Core self-hosted HTTP, Serilog, SendInput interop  
**Storage**: In-memory state + `appsettings.json` persisted configuration (no database)  
**Testing**: xUnit + Microsoft.NET.Test.Sdk with coverlet collector  
**Target Platform**: Windows 10+ desktop (win-x64) always-on-top tray utility  
**Project Type**: Single desktop/tray application (`Application.csproj` with Domain/Services/Ui folders)  
**Performance Goals**: Relay latency <500 ms per event, zero missing match/prop sections across 200 consecutive relays, winner resolved before downstream notification  
**Constraints**: Must publish single-file self-contained win-x64 build, obey FSM timing (180s auto-end, 40s defuse window), preserve ICE focus automation (Ctrl+S), enforce bearer/CIDR security, and add explanatory comments per Constitution Principle VI  
**Scale/Scope**: Single coordinator instance managing one match with hundreds of relay events per session

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- ✅ AGENTS.md + constitution reviewed; requested work already mandated (single combined relay, winner overrides).
- ✅ Platform constraints honored (.NET 9 Windows tray app, always-on-top UI, single-file win-x64 publish).
- ✅ /prop + /match contracts, FSM timing (180s auto-end, 40s defuse window) and winner override logic guide acceptance tests.
- ✅ Validation still enforces team/player name patterns; removing legacy relays does not weaken preflight enforcement.
- ✅ Relay buffering completeness and ICE focus automation impacts are captured as test requirements.
- ✅ Security posture (smart binding, bearer token, CIDR allowlist) remains intact.
- ✅ Release/testing plans protect tray visibility and packaging defaults.
- ✅ Implementation notes will ensure code changes remain heavily commented per Principle VI.

## Project Structure

### Documentation (this feature)

```text
specs/001-relay-winner-cleanup/
├─ plan.md          # this document
├─ research.md      # Phase 0 findings
├─ data-model.md    # Phase 1 entity/state definitions
├─ quickstart.md    # Phase 1 testing guide
└─ contracts/       # Phase 1 combined relay schema
```

### Source Code (repository root)

```text
repo-root/
├─ Application.csproj
├─ Program.cs
├─ Domain/
├─ Services/
├─ Http/
├─ Ui/
├─ Interop/
├─ Tests/
└─ specs/
```

**Structure Decision**: Single Windows desktop/tray application anchored at `Application.csproj`; changes touch Domain DTOs/options, Services (MatchCoordinator, RelayService, FocusService), Ui (StatusForm, MatchResultForm), Http endpoints, and Tests.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| _None_ |  |  |
