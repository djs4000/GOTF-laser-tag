# Data Model: Status Form Layout Corrections

**Branch**: 001-fix-preflight-layout  
**Date**: 2025-12-07  
**Source Spec**: specs/001-fix-preflight-layout/spec.md

## Layout Entities

### StatusFormLayoutGrid
- **Columns**: `MatchPanel`, `PropPanel`, `GameConfigPanel`
  - Match/Prop columns share equal percentages (e.g., 45% each) with minimum width 280px.
  - GameConfig column uses AutoSize but enforces minimum width 200px and anchors to the right.
- **Rows**: `HeaderRow` (Listening IPs), `ContentRow` (panels), optional `FooterRow` (status messages).
- **Constraints**: No horizontal scrollbars at 1280×720; when width < 900px, GameConfig panel stacks below others via FlowLayoutPanel fall-through.

### PreflightPanelContent
- **Fields displayed**: Team labels, validation status, descriptive text.
- **Layout rules**: Labels use AutoSize with `MaximumSize.Width = 200` and `AutoEllipsis = true`; validation text wraps inside TableLayoutPanel cell.
- **State**: Bound to existing preflight validation results; only presentation changes.

### DpiScalingConfig
- **Attributes**: `BasePadding = 8`, `ScaledPadding = BasePadding * (DeviceDpi / 96)`
- **Usage**: Applied to TableLayoutPanel `Padding` and `Margin` for dynamic DPI support.

## Flows

1. **Form Load**: Initialize TableLayoutPanel, apply DPI-aware padding, set `MinimumSize`, populate panels.
2. **Resize**: Handle `OnResize` to toggle stacked layout when `ClientSize.Width < StackThreshold`; update column styles accordingly.
3. **High DPI**: `OnCreateControl` recalculates padding/margins using `DeviceDpi` to keep content visible.
