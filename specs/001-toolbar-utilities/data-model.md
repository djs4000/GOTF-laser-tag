# Data Model: Toolbar Utilities Enhancements

**Branch**: 001-toolbar-utilities  
**Date**: 2025-12-07  
**Source Spec**: specs/001-toolbar-utilities/spec.md

## Entities

### ApplicationSettingsProfile
- **Fields**:
  - Http: object with Urls (list), BearerToken, AllowedCidrs, RequestTimeoutSeconds
  - Relay: Enabled (bool), Url, BearerToken, EnableSchemaValidation
  - Match: LtDisplayedDurationSec, AutoEndNoPlantAtSec, DefuseWindowSec, ClockExpectedHz, LatencyWindow, PreflightExpectedMatchLengthSec
  - Preflight: Enabled, ExpectedTeamNames (list), ExpectedPlayerNamePattern, EnforceMatchCancellation
  - UiAutomation: ProcessName, WindowTitleRegex, FocusTimeoutMs, PostShortcutDelayMs, DebounceWindowMs
  - Diagnostics: LogLevel, WriteToFile, LogPath
- **Relationships**: Aggregates all configuration sections so the Settings form can bind to one view-model tree.
- **Validation Rules**:
  - Http.Urls must be well-formed absolute URIs bound to active interfaces; AllowedCidrs must parse; RequestTimeoutSeconds must be greater than zero; optional BearerToken must respect length caps.
  - Relay.Url must be absolute HTTPS when deployed; BearerToken follows the same optional rules as HTTP; Enabled and EnableSchemaValidation stay boolean toggles.
  - Match.LtDisplayedDurationSec, AutoEndNoPlantAtSec, DefuseWindowSec, ClockExpectedHz, and LatencyWindow must be positive integers and respect AutoEndNoPlantAtSec ≤ LtDisplayedDurationSec while DefuseWindowSec stays at 40s unless spec changes.
  - Preflight.ExpectedTeamNames must contain exactly two entries, ExpectedPlayerNamePattern must compile, and EnforceMatchCancellation controls host cancellation behavior.
  - UiAutomation fields must produce a valid process name, regex, and positive debounce/focus delays so ICE focus automation keeps working.
  - Diagnostics.LogLevel must map to known Microsoft.Extensions.Logging levels, WriteToFile toggles file output, and LogPath must point to a writable folder when enabled.
- **State**: Maintains both persisted values and pending edits; supports change-tracking per section.

### RelaySnapshot
- **Fields**:
  - Match: latest MatchSnapshotDto (id, status, remaining_time_ms, winner_team, players, timestamp, is_last_send)
  - Prop: latest PropStatusDto (state, timestamp, uptime_ms, timer)
  - WinnerTeam, WinnerReason, Timestamp
  - LastUpdatedUtc: DateTime
- **Relationships**: Populated directly from MatchCoordinator buffering and reused by Relay Monitor.
- **Validation Rules**: Must always include both Match and Prop sections; timestamps must be monotonically non-decreasing.
- **State**: Updates whenever coordinator publishes a new snapshot; Relay Monitor reads without mutation.

### DebugPayloadTemplate
- **Fields**:
  - Name: friendly label
  - PayloadType: enum (Match, Prop, Combined)
  - JsonBody: string (validated against schema before send)
  - LastSentUtc: DateTime?
  - LastResult: enum (Success, Failed, Pending)
  - LastResponseCode: int?
- **Relationships**: Tied to the relay pipeline through IRelayService.
- **Validation Rules**: JSON must deserialize into the selected payload type; Combined payload must satisfy the same schema as production relays.
- **State**: Operators can load/save templates; UI locks while send is in progress.

## State Transitions & Flows

### Settings Update Flow
1. Load persisted ApplicationSettingsProfile into view model.  
2. Operator edits fields ? validation runs per field.  
3. On Save: validated profile is serialized back to ppsettings.json.  
4. Runtime subsystems either hot-reload immediately or display targeted restart prompts for sections flagged as restart-required (e.g., Http binding changes).  
5. Audit log entry records the change.

### Relay Monitor Flow
1. MatchCoordinator publishes MatchStateSnapshot (event).  
2. RelaySnapshot cache stores the latest CombinedRelayPayload.  
3. Relay Monitor UI receives notification, updates JSON tree fields without adding new rows.  
4. If LastUpdatedUtc exceeds stale threshold (>5s), UI highlights warning banner.

### Debug Payload Flow
1. Operator selects template or creates new payload.  
2. JSON editor validates structure against schema for selected type.  
3. When Send is pressed, UI disables controls until completion; IRelayService transmits payload using configured headers.  
4. Result (success/error) stored back into template metadata and surfaced in UI/logs.
