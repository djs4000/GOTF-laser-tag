# Feature Specification: Status Form Layout Corrections

**Feature Branch**: `001-fix-preflight-layout`  
**Created**: 2025-12-07  
**Status**: Draft  
**Input**: User description: "the status form ui is broken and the panels go off the form. see [ICE Defusal Monitor 07_12_2025 11_12_14.png 907x445] for reference. Please fix it so all the info fits within the form boundaries"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Default View Readability (Priority: P1)

Operators need the status form to show Match, Prop, and Game Configuration (including Pre-flight) panels fully within the default window so they can review match readiness at a glance without resizing or scrolling.

**Why this priority**: The status form is the primary operational surface; if content is clipped, operators miss critical readiness indicators and cannot proceed safely.

**Independent Test**: Launch the application on a standard 1280×720 display, open the status form, and verify every panel is fully visible with no controls cropped or overlapped.

**Acceptance Scenarios**:

1. **Given** the coordinator launches with default window size, **When** the status form appears, **Then** the Match, Prop, Game Configuration, and Pre-flight sections are entirely within the window bounds with consistent spacing.
2. **Given** the default view, **When** the operator switches to different toolbar tabs (Settings, Relay Monitor, Debugging), **Then** returning to the main status form still shows all panels correctly aligned without manual adjustments.

---

### User Story 2 - Responsive Panel Behavior (Priority: P2)

Operators often resize the window or operate on displays with custom DPI scaling; the layout must respond gracefully so information remains readable regardless of width changes.

**Why this priority**: Without responsive behavior the preflight information may either overflow or leave excessive whitespace, making it harder to scan during live matches.

**Independent Test**: Resize the status form between its minimum supported width and the operator’s preferred maximum, and confirm that each panel scales or reflows without clipping text or overlapping other sections.

**Acceptance Scenarios**:

1. **Given** the operator drags the window width narrower (down to the defined minimum), **When** the resize completes, **Then** panels maintain minimum widths and vertically stack or reflow without hiding fields.
2. **Given** the operator expands the window wider than default, **When** the layout recalculates, **Then** spacing and alignment remain balanced with no floating controls stranded to the right edge.

---

### Edge Cases

- How does the layout behave on high DPI displays (125%/150% scale) where font metrics change?
- What happens when the operator docks the status form beside another window, reducing width below current minimum?
- How does the system handle localization or longer team names that could expand text fields within the Game Configuration section?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The status form MUST render Match, Prop, and Game Configuration/Pre-flight panels entirely within the default window (no horizontal scrollbars or clipped controls) on a 1280×720 resolution.
- **FR-002**: Layout containers MUST adapt when the window is resized, ensuring each panel maintains a minimum width and vertically stacks when the width drops below the default arrangement.
- **FR-003**: All labels and values within the Pre-flight section MUST remain visible even when team names or validation messages reach their configured maximum length.
- **FR-004**: The layout MUST respect Windows DPI scaling between 100% and 150%, keeping controls aligned and readable without overlapping text.
- **FR-005**: Any future additions to the status form MUST plug into the same responsive layout structure so new panels automatically obey spacing and boundary rules.

### Key Entities *(include if feature involves data)*

- **Status Form Layout Grid**: Defines row/column structure, minimum widths, and padding used to position Match, Prop, and Game Configuration sections.
- **Pre-flight Panel Content**: Captures team names, validation results, and contextual descriptions that must flow within the allocated container without truncation.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: QA verifies that opening the application on a 1280×720 display shows all status panels without clipping or scrollbars (pass/fail).
- **SC-002**: Resizing the status form between its minimum width and full-screen results in zero overlapping controls or hidden fields across five consecutive tests.
- **SC-003**: Operators report zero instances of missing Pre-flight information due to layout issues over one release cycle (support ticket review).
- **SC-004**: DPI scaling tests at 125% and 150% demonstrate all panels remain readable and fully contained within the window (pass/fail).

## Assumptions

- Operators primarily use displays equal to or wider than 1280 pixels; narrower widths will trigger the responsive stacking behavior rather than maintain the three-column layout.
- Localization remains English-only for this release, but labels may reach 20 characters; layouts must accommodate that length.
- No new data fields are introduced beyond existing Match, Prop, and Pre-flight content; the work focuses strictly on layout and responsiveness.
