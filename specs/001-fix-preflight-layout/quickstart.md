# Quickstart: Status Form Layout Corrections

1. **Build & Run**  
   - `dotnet build Application.csproj -c Debug`  
   - `dotnet run --project Application.csproj`

2. **Default View Verification**  
   - With the window at its default size (1280×720 target), confirm Match, Prop, and Game Configuration/Pre-flight panels are fully visible with consistent padding.  
   - Verify the Game Configuration column stays anchored to the right without clipping labels.  
   - Capture a screenshot for regression docs (docs/status-form-default.png).

3. **Resize Behavior**  
   - Drag the right edge narrower until stacking triggers; ensure Game Configuration drops below Match/Prop without overlapping text.  
   - Expand wider than default and confirm spacing stays balanced.
   - Capture a screenshot of the stacked layout for regression docs (docs/status-form-stacked.png).

4. **DPI Scaling**  
   - On a test VM, set Windows display scale to 125% and 150%, relaunch the app, and verify no labels or values are clipped.
   - Capture a screenshot at 150% scale once validation passes (docs/status-form-dpi150.png).

5. **Toolbar Regression**  
   - Toggle to Settings / Relay Monitor / Debug tabs, then return to the main view to ensure layout state persists.

6. **Packaging Spot Check**  
   - `dotnet publish Application.csproj -c Release -r win-x64 /p:PublishSingleFile=true /p:SelfContained=true`  
   - Launch the published EXE and rerun steps 2–4 to verify release build parity.
