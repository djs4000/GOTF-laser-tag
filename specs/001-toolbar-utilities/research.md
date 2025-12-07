# Research: Toolbar Utilities Enhancements

## Decision 1: Toolbar navigation uses DI-resolved forms with centralized focus hints
- **Rationale**: Resolves WinForms lifetime management while keeping StatusForm always-on-top per Constitution.
- **Alternatives considered**: Manual `new SettingsForm()` instantiation for each button (rejected: duplicated wiring, no DI scope); hiding StatusForm while showing dialogs (rejected: violates always-on-top requirement).

## Decision 2: Relay Monitor backed by RelaySnapshot cache
- **Rationale**: Cache maintains latest match/prop pair so UI always renders complete payloads in the fixed JSON layout even if only one stream updates.
- **Alternatives considered**: Polling MatchCoordinator synchronously (rejected: extra coupling); reading log files (rejected: too slow, lacks freshness metadata).

## Decision 3: Debug Payload Injector enforces schema via existing DTOs
- **Rationale**: Deserializing JSON into known DTOs ensures parity with production payloads before sending downstream.
- **Alternatives considered**: Free-form JSON send (rejected: high risk of malformed payloads); adding new validation schema service (rejected: redundant because DTO validation already exists).
