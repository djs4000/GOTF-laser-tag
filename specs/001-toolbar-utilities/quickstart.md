# Quickstart: Toolbar Utilities Enhancements

1. **Build & Run**  
   - dotnet build Application.csproj -c Debug  
   - dotnet run --project Application.csproj (status window must appear on top with new toolbar)

2. **Toolbar Smoke Test**  
   - Confirm Settings, Relay Monitor, and Debug buttons appear with tooltips and keyboard shortcuts (Alt+S, Alt+R, Alt+D).  
   - Each button should open a modal/non-modal form without moving the status window behind other apps, and the toolbar should regain focus after the form closes.

3. **Settings Form Validation**  
   - Open Settings, modify Relay.Url, introduce an invalid CIDR to verify inline validation blocks save.  
   - Restore valid values, save, and ensure logs include a "SettingsSaved" entry.  
   - Modify Http Urls to trigger a restart-required prompt; confirm UI explains which sections require restart.

4. **Relay Monitor**  
   - Run dotnet test Tests/Tests.csproj --filter MatchCoordinator to trigger coordinator updates or send sample /match + /prop payloads via Postman.  
   - Open Relay Monitor and verify the JSON view updates field-by-field within 1 second and displays Last Updated timestamp plus a "Fresh/STALE" indicator (turns red when >5 seconds elapse without traffic).  
   - Disable Relay in Settings, reopen the monitor, and confirm the status banner explains that outbound sends are disabled but cached payloads remain visible.

5. **Debug Payload Injector**  
   - Open Debug panel, choose Combined payload, paste a sample JSON body, and use the **Format JSON** button to pretty-print before sending.  
   - Confirm downstream relay endpoint receives the payload (inspect logs) and the UI status banner turns green with the downstream HTTP code.  
   - Attempt to send malformed JSON and a request while Relay is disabled to ensure validation blocks submission with clear error messaging and red status text; verify the failure is logged.

6. **Preflight Regression**  
   - Launch Preflight UI and confirm the match-length check is absent while team/player validations remain.  
   - Document behavior in release notes if operators relied on the previous warning.

7. **Packaging Check**  
   - dotnet publish Application.csproj -c Release -r win-x64 /p:PublishSingleFile=true /p:SelfContained=true  
   - Run the published EXE and repeat toolbar/utility checks to confirm single-file distribution integrity, including verification that logs still capture Settings/Relay/Debug actions in release mode.
