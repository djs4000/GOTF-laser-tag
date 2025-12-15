# **AGENTS.md**

**Project**: Laser Tag Defusal Mode Orchestrator (Windows)

## **Agent Protocol**

**CRITICAL**: This document is the source of truth for the project's architecture and feature set.

1. **Verification**: Before implementing any code changes, verify they align with the architecture and requirements described here.  
2. **Clarification**: If a requested change contradicts this document or introduces a new feature not listed here, **you must ask the user for clarification** before proceeding.  
3. **Documentation First**: Any approved deviations or new features must be documented in this file **before** or **concurrently** with the code changes. Keep this file up-to-date.

## **Core Functionality**

* **Platform**: C\# on .NET 9; Windows desktop tray app with an always-on-top status window.  
* **Distribution**: Single-file, self-contained EXE (win-x64).  
* **Inbound HTTP (POST)**:  
  1. /prop → bomb/prop state updates (armed, planted, defusing, defused, exploded).  
  2. /match → match snapshots (status, clock, and optional player payloads from LT host).  
* **Preflight Checks**:  
  * **Strict Validation**: Validates Team Names and Player Name patterns against appsettings.json configuration.  
  * **Enforcement**: If validation fails, the application can optionally force-cancel the match (configurable).  
  * **Limitations**: Match duration validation is limited to the WaitingOnStart state as the host does not broadcast the full timer during active play.  
* **Game Logic**:  
  * If **no plant by 180 s elapsed** → end match.  
  * If **planted at ≥ 180 s elapsed**, allow **40 s overtime** (defuse window). Overtime ends after the defuse window expires or on defuse/explosion.  
  * **Exploded** or **Defused** → immediate end.  
  * **During countdown states**, ignore prop events.  
* **Relay**: Forwards a **Combined Payload** (Match \+ Prop) to a single downstream endpoint.
  * When the host transitions to **WaitingOnFinalData**, pause relay cadence entirely. Resume only to emit a single final Combined Payload once `is_last_send` is received (or after the final-data timeout elapses), then halt sending again.
* **Focus Trigger**: Targets window titled ICE (process ICombat.Desktop), restores if minimized, brings to front, then sends Ctrl+S via SendInput.

## **Topology**

\[Prop\] → POST /prop  
\[LT Host\] → POST /match  
      ↓  
   \[Application\] \--Focus+Ctrl+S--\> \[Laser Tag Software (ICE)\]  
      └──(Combined Payload)──\> \[Downstream System\]

## **Winner Determination & Override Logic**

Winners are derived solely from objective, timing, or elimination authorities and ignore any host-supplied `winner_team` values.

### **1. Objective Authority (Detonation/Defusal)**

* **Rule 10.2.5**: "Offensive team wins if it has succeeded in activating the digital flame."
* **Rule 10.2.6 (Part A)**: "Defensive team wins if... deactivated the digital flame."
* **Logic**: If the Prop reports Detonated or Defused while the match is running:
  1. Coordinator triggers Ctrl+S to end the Host match.
  2. Coordinator ignores any score-based winner the Host might calculate upon termination.
  3. Coordinator injects the objective winner into the relay payload.
* **Outcome**:
  * **Detonated**: Override -> **Attacking Team**.
  * **Defused**: Override -> **Defending Team**.

### **2. Time Authority (Expiration)**

* **Rule 10.2.6 (Part B)**: "Defensive team wins if the opposing team has failed to activate... in the allotted time."
* **Logic**:
  * **No Plant**: If the timer expires (or Auto-End threshold reached) without a plant, the Attackers failed.
    * **Outcome**: Override -> **Defending Team**.
  * **Active Plant (Overtime)**: If the timer expires but the bomb is ticking (Overtime), the match logically continues until the bomb resolves. The Coordinator waits for the final Prop state.
    * **Outcome**: Depends on Prop (Detonate/Defuse).

### **3. Team Elimination**

* **Logic**: If one team has living players while the opposing team has none (and both teams are present in the snapshot), the living team wins.
* **Outcome**: **Team Elimination Winner**.
## **HTTP API**

Base URL configurable (e.g., http://127.0.0.1:5055/)

### **Smart Binding**

The application automatically discovers active network interfaces on startup. It will only bind to IP addresses that are operationally **Up**. If a configured URL binds to an inactive interface, it is skipped to prevent startup failures.

### **Authentication**

* Header: Authorization: Bearer \<TOKEN\> (optional if disabled)  
* 401 on invalid token or denied IP (CIDR allowlist).

### **/prop**

**Body (JSON)**  
{  
  "timestamp": 1761553673,  
  "uptime\_ms": 1234567,  
  "state": "planted" // "armed", "defusing", "defused", "exploded"  
}

### **/match**

**Body (JSON)**  
{  
  "id": "match\_identifier",  
  "timestamp": 1761553673,  
  "status": "Running",  
  "remaining\_time\_ms": 180000,  
  "winner\_team": null,  
  "is\_last\_send": false,  
  "players": \[ ... \]  
}

### **Relay (outbound)**

The application forwards a **Combined Payload** to the configured Relay:Url.

* **Payload**: Contains both match (MatchSnapshotDto) and prop (PropStatusDto) objects.  
* **Method**: POST  
* **State Preservation (Cadence Handling)**:  
  * The Prop reports at \~500ms intervals (configurable).  
  * The Laser Tag Host reports at \~100ms intervals (configurable).  
  * **Requirement**: The Coordinator must buffer the latest state from each source. Every relay payload must contain fully populated match and prop objects (using the latest known data). Do not send payloads with null/empty fields for the component that did not trigger the immediate update.  
* **Winner Override**: Applied here before transmission.

## **State Machine**

stateDiagram-v2  
    \[\*\] \--\> Idle  
    Idle \--\> Armed: prop=Armed  
    Armed \--\> Planted: prop=Planted (record plantTime)  
    Planted \--\> Defusing: prop=Defusing  
    Defusing \--\> Defused: prop=Defused  
    Armed \--\> Exploded: prop=Exploded  
    Planted \--\> Exploded: prop=Exploded  
    Defusing \--\> Exploded: prop=Exploded  
    Exploded \--\> End  
    Defused \--\> End  
    Armed \--\> End: elapsed\>=180 && \!planted  
    Planted \--\> End: elapsed \>= plantTime \+ 40  
    Defusing \--\> End: elapsed \>= plantTime \+ 40  
    End \--\> \[\*\]

## **Status Window & UI**

* **Status Form**: Always-on-top panel showing MatchId, FSM state, OVERTIME badge, timers, and a persistent ToolStrip toolbar that exposes Settings, Relay Monitor, and Debugging buttons with mouse and keyboard access.  
  * Layout guardrail: Match, Prop, and Game Configuration/Pre-flight panels must remain fully visible within the default 1280×720 window; Game Configuration column may stack beneath others when width <1,100px but must never clip content.  
* **Match Results Popup**:  
  * **Trigger**: Appears when match transitions to Completed.  
  * **Content**: Displays Winning Team, Role (Attacking vs Defending), and Reason (e.g., "Bomb Detonated").  
  * **Payload**: Shows raw JSON of the final relayed payload.  
* **Settings Form**: Launchable from the toolbar; must surface every `appsettings.json` section with inline validation, persistence back to disk, immediate application when safe, and restart prompts for sections (e.g., HTTP bindings) that cannot hot-reload.  
* **Relay Monitor**: Launchable from the toolbar; renders the incoming Match and Prop JSON alongside the outgoing Combined Relay Payload in a fixed JSON layout fed by the coordinator buffer, preserves scroll position (tail-follows when scrolled to the bottom), updates values within one second, and highlights stale data when no payload has arrived for more than five seconds.
* **Debug Payload Panel**: Launchable from the toolbar; lets operators craft match/prop/combined payloads, validates JSON, and submits through the existing RelayService with shared authentication headers, including success/failure feedback and diagnostics logging.  
* **Preflight UI**:  
  * Displays status of checks (Team names, Player names) without enforcing a match-length validation prior to countdown because hosts no longer broadcast that value ahead of time.  
  * Indicators turn Green (Pass) or Red (Fail).
* **Diagnostics & Logging**: Settings saves emit structured log entries that include restart-required sections; Relay Monitor logs when payload freshness transitions between fresh/stale states; Debug payload sends log payload type, HTTP status, and error details when submissions fail.

## **Configuration**
```json
{  
  "Http": {  
    "Urls": \["\[http://127.0.0.1:5055\](http://127.0.0.1:5055)"\],  
    "BearerToken": "CHANGE\_ME",  
    "AllowedCidrs": \["127.0.0.1/32", "192.168.10.0/24"\]  
  },  
  "Relay": {   
    "Enabled": false,   
    "Url": "http://relay-endpoint/combined",   
    "BearerToken": null   
  },  
  "Match": {  
    "LtDisplayedDurationSec": 219,  
    "AutoEndNoPlantAtSec": 180,  
    "DefuseWindowSec": 40,  
    "ClockExpectedHz": 10  
  },  
  "Preflight": {  
    "Enabled": true,  
    "ExpectedTeamNames": \["Team 1", "Team 2"\],  
    "ExpectedPlayerNamePattern": "^\[0-9\]+$",   
    "EnforceMatchCancellation": false  
  },  
  "UiAutomation": {  
    "ProcessName": "ICombat.Desktop",  
    "WindowTitleRegex": "^ICE$",  
    "DebounceWindowMs": 2000  
  },  
  "Diagnostics": {  
    "LogLevel": "Information",  
    "WriteToFile": true  
  }  
}
```

## **Folder Layout**

/ (repo root)  
  ├─ Application.csproj  
  ├─ Program.cs                 \# Startup, Smart Binding  
  ├─ Http/                      \# Endpoints, Middleware  
  ├─ Domain/                    \# FSM, Options, DTOs  
  ├─ Ui/                        \# StatusForm, MatchResultForm  
  ├─ Interop/                   \# NativeMethods  
  ├─ Services/                  \# MatchCoordinator, RelayService, FocusService  
  ├─ appsettings.json  
  └─ agents.md (this file)

