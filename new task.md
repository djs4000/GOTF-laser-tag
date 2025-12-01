**Task:** Implement a "Match Results" popup dialog that appears when a match concludes.
**Role:** Software Engineer (C# / .NET 9 / WinForms)

**Context:**
The application (`MatchCoordinator`) tracks the state of a laser tag match. Currently, it updates a `StatusForm` with real-time data. We need to intercept the final state of the match, specifically the final payload sent to the relay, and display a summary to the user.

**Requirements:**

1.  **Backend Updates (`MatchStateSnapshot` & `MatchCoordinator`):**
    * Modify `MatchStateSnapshot` (in `Domain/MatchStateSnapshot.cs`) to include a new property: `MatchSnapshotDto? LatestRelayPayload`.
    * Update `MatchCoordinator.PublishSnapshotLocked` (in `Services/MatchCoordinator.cs`) to populate this new property with the `matchRelayPayload` that is constructed (and potentially modified/winner-overridden) before it is sent to the `RelayService`. This ensures the UI has access to the exact data sent downstream.

2.  **New UI Component (`Ui/MatchResultForm.cs`):**
    * Create a new Windows Form named `MatchResultForm`.
    * **Constructor Inputs:** It should accept the `MatchStateSnapshot`, the `AttackingTeam` name (string), and the `DefendingTeam` name (string).
    * **Display Fields:**
        * **Winning Team:** Display the team name from the payload (`WinnerTeam`).
        * **Role:** Determine and display if the winning team was **Attacking** or **Defending** (compare `WinnerTeam` against the passed `AttackingTeam` name).
        * **Reason:** Display the cause of the match end.
            * *Logic:* Check `Snapshot.LastActionDescription`. If it starts with "Triggering end:", use that text. Otherwise, derive it from `Snapshot.PropState` (e.g., "Bomb Detonated", "Bomb Defused") or default to "Time Expired / Host Ended".
        * **Final Payload:** A readonly multi-line text box displaying the JSON content of `LatestRelayPayload`. Use `System.Text.Json` with `WriteIndented = true` to pretty-print it.

3.  **Integration (`Ui/StatusForm.cs`):**
    * In `StatusForm`, monitor the `MatchLifecycleState` within the `RenderSnapshot` (or `OnSnapshotUpdated`) method.
    * Detect the transition from a non-terminal state (e.g., `Running`, `WaitingOnFinalData`) to `MatchLifecycleState.Completed` (or `Cancelled`).
    * **Trigger:** When this transition occurs, instantiate and show the `MatchResultForm` using `Show()` (non-blocking) or `ShowDialog()`.
    * **Data Source:** Pass the current snapshot (which now contains the `LatestRelayPayload`) and the team names from `_coordinator` properties to the form.
    * **Constraint:** Ensure the popup only appears once per match (e.g., track a `_hasShownResults` flag that resets when the match state goes back to `Idle` or `WaitingOnStart`).

**Implementation Details & Hints:**
* **JSON Serialization:** You can reuse the global serializer options or create a new `JsonSerializerOptions { WriteIndented = true }` for the text box display.
* **Threading:** Remember that `MatchCoordinator` events fire on background threads. Ensure UI updates and Form instantiation happen on the UI thread (Invoke/BeginInvoke is already used in `StatusForm`, just ensure the new form is launched safely).
* **Safety:** Handle cases where `LatestRelayPayload` might be null (e.g., if the relay is disabled or no payload was generated). In this case, display "No payload sent" in the text box.