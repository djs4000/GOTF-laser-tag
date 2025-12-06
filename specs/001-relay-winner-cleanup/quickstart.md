# Quickstart: Relay Winner Cleanup

1) Build and run
- `dotnet build Application.csproj -c Release`
- Run produced single-file win-x64 app (or `dotnet run --project Application.csproj`) and ensure tray/status window visible.

2) Configure appsettings.json
- Confirm Relay.Enabled points to downstream endpoint.
- Combined relay default is `http://relay-endpoint/combined`; override if your downstream differs.
- Verify Http.Urls use active interfaces; BearerToken/CIDR allowlist set for local testing.

3) Smoke test relay consolidation
- Send /prop and /match sample payloads (curl/Postman) and confirm only combined relay endpoint receives data.
- Inspect logs to ensure no legacy relay endpoints are registered or invoked.

4) Validate winner logic
- Scenario A (team wipe): send host status Completed with WinnerTeam before any prop resolution; confirm relay winner matches host.
- Scenario B (objective): send running match then prop Exploded/Defused; confirm winner override per objective and host winner ignored.
- Scenario C (timeout): simulate 180s no plant; confirm defenders win and winner_reason=TimeExpiration.

5) Payload completeness
- Send alternating /match and /prop at different cadences; verify every relay payload includes non-null match and prop sections with latest buffered values.

6) UI/status checks
- Status window stays always-on-top and reflects FSM/winner result; match results popup shows winner_team and reason consistent with payload.
