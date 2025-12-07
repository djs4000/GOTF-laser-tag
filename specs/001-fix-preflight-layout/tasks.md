# Tasks: Status Form Layout Corrections

**Input**: Design documents from `/specs/001-fix-preflight-layout/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, quickstart.md

**Tests**: Manual UI validation per quickstart (default view, resize behavior, DPI scaling). Automated UI tests are optional and not required by the spec.

**Organization**: Tasks are grouped by user story so each increment can ship independently. Code comments must explain layout intent per AGENTS Constitution.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Task can proceed in parallel (different files, no dependency)
- **[Story]**: Label maps to the applicable user story (US1, US2)
- Include absolute file paths in every description

## Phase 1: Setup (Shared Context)

**Purpose**: Reconfirm layout constraints and guardrails before editing StatusForm.

- [X] T001 Document current StatusForm layout breakpoints and captured screenshot references inside `D:\Documents\Code Project\GOTF-laser-tag\specs\001-fix-preflight-layout\research.md`.
- [X] T002 Review AGENTS.md layout-related rules and annotate the sections affecting StatusForm edits within `D:\Documents\Code Project\GOTF-laser-tag\agents.md`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Establish reusable layout helpers and constants shared by both user stories.

- [X] T003 Create DPI-aware padding/spacing constants and helper methods in `D:\Documents\Code Project\GOTF-laser-tag\Ui\StatusForm.cs` for reuse across panels.
- [X] T004 Add a StatusForm `MinimumSize` and document the rationale in `D:\Documents\Code Project\GOTF-laser-tag\Ui\StatusForm.cs` so future controls respect the baseline width.

---

## Phase 3: User Story 1 - Default View Readability (Priority: P1)

**Goal**: Ensure Match, Prop, and Game Configuration/Pre-flight panels are fully visible in the default window.

**Independent Test**: Launch at 1280x720 and verify all sections fit with consistent padding; capture screenshot noted in quickstart.

### Implementation

- [X] T005 [US1] Refactor the main content container to a TableLayoutPanel with explicit column styles inside `D:\Documents\Code Project\GOTF-laser-tag\Ui\StatusForm.cs`.
- [X] T006 [US1] Anchor and size the Game Configuration/Pre-flight column (minimum 200px) and ensure labels use AutoEllipsis in `D:\Documents\Code Project\GOTF-laser-tag\Ui\StatusForm.cs`.
- [X] T007 [US1] Update Pre-flight status label rendering to wrap text without clipping by adjusting controls in `D:\Documents\Code Project\GOTF-laser-tag\Ui\StatusForm.cs`.
- [X] T008 [US1] Refresh documentation describing the default layout behavior in `D:\Documents\Code Project\GOTF-laser-tag\specs\001-fix-preflight-layout\quickstart.md`.

**Checkpoint**: Default-sized form fully displays every panel and text block.

---

## Phase 4: User Story 2 - Responsive Panel Behavior (Priority: P2)

**Goal**: Provide graceful stacking/resizing when the window width or DPI changes.

**Independent Test**: Resize between minimum width and full-screen plus test at 125%/150% DPI; no clipping or overlap observed.

### Implementation

- [X] T009 [US2] Implement `OnResize` logic that toggles stacked layout when client width < threshold within `D:\Documents\Code Project\GOTF-laser-tag\Ui\StatusForm.cs`.
- [X] T010 [US2] Ensure Game Configuration panel migrates below Match/Prop without losing data bindings by updating container rules in `D:\Documents\Code Project\GOTF-laser-tag\Ui\StatusForm.cs`.
- [X] T011 [US2] Apply DPI-scaling adjustments (using helper constants) during `OnCreateControl` or equivalent initialization in `D:\Documents\Code Project\GOTF-laser-tag\Ui\StatusForm.cs`.
- [X] T012 [US2] Document resizing/DPI validation steps and screenshots in `D:\Documents\Code Project\GOTF-laser-tag\specs\001-fix-preflight-layout\quickstart.md`.

**Checkpoint**: Resizing and DPI scaling maintain readable layout with no clipped panels.

---

## Phase 5: Polish & Cross-Cutting Concerns

**Purpose**: Final verification and documentation updates once layout changes land.

- [X] T013 Update `D:\Documents\Code Project\GOTF-laser-tag\specs\001-fix-preflight-layout\plan.md` with final layout decisions and any tradeoffs learned during implementation.
- [X] T014 Capture before/after screenshots and place references in `D:\Documents\Code Project\GOTF-laser-tag\specs\001-fix-preflight-layout\quickstart.md` (or `/docs` if preferred).
- [X] T015 Ensure `D:\Documents\Code Project\GOTF-laser-tag\Ui\StatusForm.cs` contains refreshed explanatory comments for every modified layout block per Constitution guidelines.

---

## Dependencies & Execution Order

1. Phase 1 (Setup) ? must complete before layout edits.
2. Phase 2 (Foundational) ? depends on Phase 1; provides shared helpers.
3. Phase 3 (US1) ? depends on Phase 2.
4. Phase 4 (US2) ? depends on Phase 3 since stacking builds on new layout grid.
5. Phase 5 (Polish) ? after all user stories.

### Parallel Execution Examples

- During Phase 3, T005 (layout grid) and T006 (game config sizing) should run sequentially, but T007 (pre-flight labels) can start once T005 lands.
- In Phase 4, T009 (resize logic) and T011 (DPI scaling) can be developed concurrently after the stacking container exists.

## Implementation Strategy

- **MVP**: Complete US1 tasks (T005–T008) to deliver a default view that fits without clipping.
- **Incremental**: Layer responsive behavior (US2) once MVP verified, then finalize polish/docs.
- **Documentation**: Keep quickstart screenshots updated for regression-proofing.
