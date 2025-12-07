# Research: Status Form Layout Corrections

## Decision 1: Use nested TableLayoutPanels with explicit ColumnStyles
- **Rationale**: TableLayoutPanels allow proportional columns plus absolute widths for the Game Configuration column so controls never overflow; easier to adjust than manual pixel positioning.
- **Alternatives considered**: Absolute positioning (rejected: brittle under DPI changes); SplitContainer (rejected: adds resize grips we do not want).

## Decision 2: Enforce minimum window width + auto-stacking
- **Rationale**: Setting `MinimumSize` on StatusForm plus docking the right-hand panel into a FlowLayoutPanel lets the UI stack vertically when width is constrained while preserving readability.
- **Alternatives considered**: Scrollbars (rejected: spec requires everything visible without scrolling); font scaling hacks (rejected: would conflict with accessibility expectations).

## Decision 3: DPI-aware padding constants
- **Rationale**: Use `ScaleControl` or `DeviceDpi` to compute padding/margins so 125%/150% scaling does not clip Pre-flight text.
- **Alternatives considered**: Hard-coded pixel padding (rejected: fails on high DPI).

## Observed Breakpoints & References
- Default launch at 1280x720 currently shows the Pre-flight card cropped on the right (see screenshot `ICE Defusal Monitor 07_12_2025 11_12_14.png`).
- Layout begins clipping once client width < 1180px; Game Configuration column stops rendering entirely when < 1100px.
- Operators typically resize up to ~1600px width where large gaps appear between Prop and Game Configuration cards; we will retain proportional spacing to avoid this.
