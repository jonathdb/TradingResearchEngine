# Implementation Plan: UI Charting & UX Improvements

## Overview

This plan implements 13 UI/UX improvements across the TradingResearchEngine Blazor frontend. Tasks are ordered so pure computation helpers and enums (no UI dependencies) come first, followed by property-based tests, then Blazor component modifications, and finally unit tests for components.

## Tasks

- [x] 1. Implement pure computation helpers and enum
  - [x] 1.1 Create the ResearchStepStatus enum
    - Add `ResearchStepStatus.cs` to `src/TradingResearchEngine.Application/Research/`
    - Define enum with values: `NotStarted`, `InProgress`, `Completed`
    - Add XML doc comments
    - _Requirements: 9.1, 9.2, 9.3_

  - [x] 1.2 Implement ChartComputationHelpers methods
    - Add `ComputeYAxisRange(decimal minEquity, decimal maxEquity)` returning `(decimal Lower, decimal Upper)` with ±1% padding
    - Add `HeatmapAnnotation` record type `(int Year, int Month, string Text)`
    - Add `ComputeHeatmapAnnotations(IReadOnlyList<MonthlyReturn> returns)` returning formatted annotation strings (one decimal place + %)
    - Add `ComputeHeatmapHeight(int yearCount)` returning at least `yearCount * 30` pixels
    - Add `ComputeProgressPercent(IReadOnlyList<ResearchStepStatus> steps)` returning completed/total * 100
    - Add `ComputeProgressText(IReadOnlyList<ResearchStepStatus> steps)` returning "{X} of 9 completed"
    - Add `StepDescriptions` readonly dictionary for all 9 research steps
    - All methods are pure static functions with XML doc comments
    - _Requirements: 3.1, 5.2, 5.3, 6.1, 10.1, 10.2, 11.2_

- [ ] 2. Property-based tests for computation helpers
  - [ ]* 2.1 Write property test for Y-Axis Range Padding
    - **Property 1: Y-Axis Range Padding**
    - For any positive min/max where max >= min, `ComputeYAxisRange` returns `(min * 0.99, max * 1.01)`
    - Use FsCheck.Xunit with `[Property(MaxTest = 100)]`
    - Tag: `// Feature: ui-charting-ux-improvements, Property 1: Y-Axis Range Padding`
    - **Validates: Requirements 3.1**

  - [ ]* 2.2 Write property test for Heatmap Annotation Completeness
    - **Property 2: Heatmap Annotation Completeness**
    - For any set of monthly returns, annotation count equals input count and each text matches `"{value:F1}%"` format
    - Use FsCheck.Xunit with `[Property(MaxTest = 100)]`
    - Tag: `// Feature: ui-charting-ux-improvements, Property 2: Heatmap Annotation Completeness`
    - **Validates: Requirements 5.3**

  - [ ]* 2.3 Write property test for Heatmap Color Scale Bounds
    - **Property 3: Heatmap Color Scale Bounds Match Data Range**
    - For any non-empty set of monthly returns, color scale min/max equals actual data min/max
    - Use FsCheck.Xunit with `[Property(MaxTest = 100)]`
    - Tag: `// Feature: ui-charting-ux-improvements, Property 3: Heatmap Color Scale Bounds Match Data Range`
    - **Validates: Requirements 6.1**

  - [ ]* 2.4 Write property test for Heatmap Dynamic Height
    - **Property 4: Heatmap Dynamic Height Accommodates All Years**
    - For any year count N >= 1, computed height >= N * 30 pixels
    - Use FsCheck.Xunit with `[Property(MaxTest = 100)]`
    - Tag: `// Feature: ui-charting-ux-improvements, Property 4: Heatmap Dynamic Height Accommodates All Years`
    - **Validates: Requirements 5.2**

  - [ ]* 2.5 Write property test for Research Progress Indicator Accuracy
    - **Property 5: Research Progress Indicator Accuracy**
    - For any combination of 9 step states, progress value equals `completedCount / 9 * 100` and text reads `"{completedCount} of 9 completed"`
    - Use FsCheck.Xunit with `[Property(MaxTest = 100)]`
    - Tag: `// Feature: ui-charting-ux-improvements, Property 5: Research Progress Indicator Accuracy`
    - **Validates: Requirements 10.1, 10.2**

- [x] 3. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Modify EquityCurveChart component
  - [x] 4.1 Reduce drawdown overlay visual dominance
    - Set drawdown fill opacity to 0.15
    - Set equity line width to at least 2px with z-order above drawdown fill
    - Set drawdown line color opacity to no more than 0.4
    - _Requirements: 1.1, 1.2, 1.3_

  - [x] 4.2 Tighten Y-axis scaling
    - Call `ChartComputationHelpers.ComputeYAxisRange` to compute tight bounds
    - Apply computed range to the Plotly Y-axis configuration
    - _Requirements: 3.1, 3.2_

  - [x] 4.3 Add interactive crosshair tooltips
    - Configure Plotly spike lines for vertical crosshair at cursor X-position
    - Configure hover template to show date, equity (currency format), and drawdown % (1 decimal place)
    - When `ShowDrawdown` is false, tooltip shows date and equity only
    - _Requirements: 4.1, 4.2, 4.3_

- [x] 5. Modify MonthlyReturnsHeatmap component
  - [x] 5.1 Improve heatmap readability
    - Set Y-axis font size to minimum 12px
    - Compute dynamic chart height using `ChartComputationHelpers.ComputeHeatmapHeight`
    - Add text annotations inside cells using `ChartComputationHelpers.ComputeHeatmapAnnotations`
    - Configure hover template to show year, month, and return % to 2 decimal places
    - _Requirements: 5.1, 5.2, 5.3, 5.4_

  - [x] 5.2 Normalise color scale to actual data range
    - Set color scale min/max from actual data values (not fixed range)
    - Set ZMid to zero for green/red diverging scale
    - Ensure all-positive datasets still show color variation across green spectrum
    - _Requirements: 6.1, 6.2, 6.3_

- [x] 6. Modify MetricCard and StrategyOverviewPanel components
  - [x] 6.1 Add IsPrimary parameter to MetricCard
    - Add `[Parameter] public bool IsPrimary { get; set; } = false;`
    - When true, render value at `Typo.h5` weight with increased card padding
    - When false, render at standard `Typo.h6` weight
    - _Requirements: 7.2_

  - [x] 6.2 Update StrategyOverviewPanel layout and hierarchy
    - Render Confidence_Badge at 1.5x size in strategy header area adjacent to strategy name
    - Mark Sharpe Ratio MetricCard as `IsPrimary=true`
    - Render secondary metrics (Max DD, Trades, Win Rate) at standard size
    - Consolidate to single EquityCurveChart with drawdown toggle control
    - Remove separate drawdown chart instance
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 7.1, 7.3, 7.4_

  - [x] 6.3 Reposition RobustnessWarnings above chart
    - Move RobustnessWarnings component above EquityCurveChart in StrategyOverviewPanel
    - Render as full-width horizontal banner
    - Render nothing when no warnings exist (no vertical space consumed)
    - _Requirements: 8.1, 8.2, 8.3_

- [x] 7. Modify ResearchChecklist component
  - [x] 7.1 Implement three visual states
    - Completed: green checkmark icon, full-opacity text
    - InProgress: amber/orange pulsing icon, full-opacity text
    - NotStarted: grey circle outline icon, 0.5 opacity text
    - Update parameter type to accept model with `ResearchStepStatus` per step
    - _Requirements: 9.1, 9.2, 9.3, 9.4_

  - [x] 7.2 Add inline progress indicator
    - Add MudProgressLinear above checklist items showing completed/total ratio
    - Display completion count text: "{X} of 9 completed"
    - Use `ChartComputationHelpers.ComputeProgressPercent` and `ComputeProgressText`
    - Render progress bar green at 100%
    - _Requirements: 10.1, 10.2, 10.3_

  - [x] 7.3 Add step description tooltips
    - Add MudTooltip on each step label using `ChartComputationHelpers.StepDescriptions`
    - Provide descriptions for all 9 checklist steps
    - _Requirements: 11.1, 11.2_

- [x] 8. Modify NavMenu component
  - [x] 8.1 Improve navigation sidebar hierarchy
    - Render section headers (DASHBOARD, STRATEGIES, RESEARCH, etc.) with uppercase, smaller font, muted colour (Typo.overline)
    - Render child links with standard body typography and left indentation >= 16px
    - Add chevron icon to expandable groups that rotates on expand/collapse
    - _Requirements: 12.1, 12.2, 12.3_

- [x] 9. Modify StrategyDetail page for version selector
  - [x] 9.1 Make Version Selector visually interactive
    - Apply `Variant.Outlined` with visible border and distinct background colour
    - Add dropdown indicator icon
    - Add hover state with background colour change or border highlight
    - _Requirements: 13.1, 13.2_

- [x] 10. Checkpoint - Ensure all component modifications compile
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 11. Unit tests for component configurations
  - [ ]* 11.1 Write unit tests for EquityCurveChart configuration
    - Test drawdown opacity <= 0.15
    - Test equity line width >= 2
    - Test drawdown line color opacity <= 0.4
    - Test crosshair/spike line configuration
    - Test conditional tooltip content (with/without drawdown)
    - _Requirements: 1.1, 1.2, 1.3, 4.1, 4.2, 4.3_

  - [ ]* 11.2 Write unit tests for MonthlyReturnsHeatmap configuration
    - Test font size >= 12px
    - Test ZMid = 0
    - Test all-positive data still shows color variation
    - Test hover template format
    - _Requirements: 5.1, 6.2, 6.3, 5.4_

  - [ ]* 11.3 Write unit tests for MetricCard
    - Test IsPrimary=true renders Typo.h5
    - Test IsPrimary=false renders Typo.h6
    - _Requirements: 7.2_

  - [ ]* 11.4 Write unit tests for ResearchChecklist
    - Test three visual states (icon + opacity per state)
    - Test all 9 step descriptions present
    - Test progress bar at 100% when all complete
    - _Requirements: 9.1, 9.2, 9.3, 10.3, 11.2_

  - [ ]* 11.5 Write unit tests for RobustnessWarnings
    - Test renders nothing when no warnings
    - Test renders full-width banner when warnings exist
    - _Requirements: 8.2, 8.3_

  - [ ]* 11.6 Write unit tests for NavMenu
    - Test section headers use Typo.overline
    - Test child links indented >= 16px
    - _Requirements: 12.1, 12.2_

  - [ ]* 11.7 Write unit tests for VersionSelector
    - Test Variant.Outlined applied
    - Test hover class present
    - _Requirements: 13.1, 13.2_

- [x] 12. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific component configurations and edge cases
- All property tests use FsCheck.Xunit with minimum 100 iterations
- Test classes follow naming convention: `ChartComputationHelpersProperties`, `ResearchChecklistProperties`, `EquityCurveChartTests`, etc.
- All tests live in `src/TradingResearchEngine.UnitTests/` referencing Application only (not Web)
