# Requirements Document

## Introduction

This specification defines UI charting and UX improvements for the TradingResearchEngine Blazor web frontend. The improvements address visual hierarchy, chart readability, interactive data exploration, and component usability across the equity curve charts, monthly returns heatmap, strategy overview panel, research progress checklist, and navigation layout. The project uses Plotly.Blazor for charting, MudBlazor for UI components, and follows a dark theme.

## Glossary

- **Equity_Curve_Chart**: The Plotly.Blazor `EquityCurveChart.razor` component that renders total equity over time with an optional drawdown overlay on a secondary Y-axis
- **Drawdown_Overlay**: The filled area trace on the equity curve chart representing peak-to-trough percentage decline, rendered on the right Y-axis
- **Monthly_Returns_Heatmap**: The Plotly.Blazor `MonthlyReturnsHeatmap.razor` component that renders a color-coded grid of monthly return percentages by year and month
- **Strategy_Overview_Panel**: The section of the strategy detail page containing KPI metric cards, robustness warnings, and the confidence level badge
- **MetricCard**: The `MetricCard.razor` shared component that displays a single KPI with a label and value
- **Research_Checklist**: The `ResearchChecklist.razor` shared component that displays the 9-step research progress pipeline with a confidence level indicator
- **Robustness_Warnings**: The `RobustnessWarnings.razor` shared component that displays automatic warning badges based on backtest metric thresholds
- **Confidence_Badge**: The MudChip element within the Research_Checklist that shows the confidence level (HIGH/MEDIUM/LOW) and passed count
- **NavMenu**: The `NavMenu.razor` layout component providing left sidebar navigation with collapsible groups
- **Crosshair_Tooltip**: An interactive hover element on a chart that displays precise data values (date, equity, drawdown%) at the cursor position
- **Version_Selector**: The UI element that allows users to select between strategy versions

## Requirements

### Requirement 1: Reduce Drawdown Overlay Visual Dominance

**User Story:** As a trader reviewing strategy performance, I want the equity line to be the visual hero of the chart, so that I can quickly assess overall performance without the drawdown fill obscuring the signal.

#### Acceptance Criteria

1. WHEN the Equity_Curve_Chart renders with ShowDrawdown enabled, THE Equity_Curve_Chart SHALL set the drawdown fill opacity to no more than 0.15
2. WHEN the Equity_Curve_Chart renders with ShowDrawdown enabled, THE Equity_Curve_Chart SHALL render the equity line with a line width of at least 2 pixels and a z-order above the drawdown fill
3. WHEN the Equity_Curve_Chart renders with ShowDrawdown enabled, THE Equity_Curve_Chart SHALL use a drawdown line color with opacity no greater than 0.4

### Requirement 2: Consolidate Duplicate Equity and Drawdown Charts

**User Story:** As a trader viewing strategy results, I want a single equity chart with a toggle for the drawdown overlay, so that screen real estate is used efficiently without redundant visualisations.

#### Acceptance Criteria

1. THE Strategy_Overview_Panel SHALL display a single Equity_Curve_Chart instance instead of separate equity and drawdown charts
2. WHEN the strategy detail page renders, THE Strategy_Overview_Panel SHALL provide a toggle control that enables or disables the drawdown overlay on the single Equity_Curve_Chart
3. WHEN the user disables the drawdown overlay via the toggle, THE Equity_Curve_Chart SHALL remove the drawdown trace and secondary Y-axis from the rendered chart
4. WHEN the user enables the drawdown overlay via the toggle, THE Equity_Curve_Chart SHALL add the drawdown trace with the reduced-opacity fill defined in Requirement 1

### Requirement 3: Tighten Equity Curve Y-Axis Scaling

**User Story:** As a trader analysing equity performance, I want the Y-axis to fit tightly around the actual data range, so that small but meaningful equity movements are visually distinguishable.

#### Acceptance Criteria

1. WHEN the Equity_Curve_Chart renders, THE Equity_Curve_Chart SHALL set the Y-axis range to span from 1% below the minimum equity value to 1% above the maximum equity value
2. WHEN the equity curve data range is less than 5% of the minimum value, THE Equity_Curve_Chart SHALL apply the tight Y-axis scaling to prevent visual flattening of the signal

### Requirement 4: Add Interactive Crosshair Tooltips to Equity Curve

**User Story:** As a trader exploring equity curve data, I want crosshair tooltips showing date, equity value, and drawdown percentage on hover, so that I can read precise values at any point without relying on axis labels alone.

#### Acceptance Criteria

1. WHEN the user hovers over the Equity_Curve_Chart, THE Equity_Curve_Chart SHALL display a vertical crosshair line at the cursor X-position
2. WHEN the user hovers over the Equity_Curve_Chart, THE Equity_Curve_Chart SHALL display a tooltip containing the date, equity value formatted as currency, and drawdown percentage formatted to one decimal place
3. WHEN the Equity_Curve_Chart has ShowDrawdown disabled, THE Equity_Curve_Chart SHALL display the tooltip with date and equity value only

### Requirement 5: Improve Monthly Returns Heatmap Readability

**User Story:** As a trader reviewing monthly performance patterns, I want year labels to be legible and cell values visible, so that I can identify seasonal patterns and return magnitudes without squinting or relying solely on color.

#### Acceptance Criteria

1. THE Monthly_Returns_Heatmap SHALL render year labels on the Y-axis at a minimum font size of 12 pixels
2. THE Monthly_Returns_Heatmap SHALL set a minimum row height of 30 pixels per year to prevent label overlap
3. WHEN a heatmap cell contains a non-null return value, THE Monthly_Returns_Heatmap SHALL display the percentage value as text annotation inside the cell formatted to one decimal place
4. WHEN the user hovers over a heatmap cell, THE Monthly_Returns_Heatmap SHALL display a tooltip showing the year, month, and return percentage formatted to two decimal places

### Requirement 6: Normalise Heatmap Color Scale to Actual Data Range

**User Story:** As a trader comparing monthly returns, I want the color scale to reflect the actual data range, so that colour variation is meaningful and not compressed by an arbitrary midpoint.

#### Acceptance Criteria

1. WHEN the Monthly_Returns_Heatmap renders, THE Monthly_Returns_Heatmap SHALL compute the color scale minimum and maximum from the actual data values rather than using a fixed range
2. THE Monthly_Returns_Heatmap SHALL set the color scale midpoint (ZMid) to zero so that positive returns render green and negative returns render red regardless of the data range
3. WHEN all monthly return values are positive, THE Monthly_Returns_Heatmap SHALL still render colour variation across the green spectrum proportional to magnitude

### Requirement 7: Establish KPI Card Visual Hierarchy

**User Story:** As a trader scanning strategy metrics, I want the most important KPI (Confidence Level) to be visually dominant, so that I can immediately assess strategy readiness without scanning all cards equally.

#### Acceptance Criteria

1. THE Strategy_Overview_Panel SHALL render the Confidence_Badge at a larger size (minimum 1.5x the standard MetricCard text size) and position it in the strategy header area adjacent to the strategy name
2. THE MetricCard component SHALL accept an optional IsPrimary parameter that, when true, renders the value text at Typo.h5 weight and increases the card padding
3. WHEN the Strategy_Overview_Panel renders KPI cards, THE Strategy_Overview_Panel SHALL mark the Sharpe Ratio card as primary using the IsPrimary parameter
4. THE Strategy_Overview_Panel SHALL render secondary metrics (Max DD, Trades, Win Rate) at the standard MetricCard size below or beside the primary metric

### Requirement 8: Reposition Robustness Warnings Above Chart

**User Story:** As a trader reviewing strategy results, I want robustness warnings displayed prominently above the equity chart, so that critical warnings are seen before the performance visualisation rather than competing with it inline.

#### Acceptance Criteria

1. WHEN robustness warnings exist for a strategy result, THE Strategy_Overview_Panel SHALL render the Robustness_Warnings component above the Equity_Curve_Chart in the page layout
2. THE Robustness_Warnings component SHALL render as a horizontal banner spanning the full width of the content area
3. WHEN no robustness warnings exist, THE Robustness_Warnings component SHALL render nothing and consume no vertical space

### Requirement 9: Add Three Visual States to Research Checklist

**User Story:** As a trader tracking research progress, I want distinct visual states for not-started, in-progress, and completed steps, so that I can immediately see which research phases need attention.

#### Acceptance Criteria

1. THE Research_Checklist SHALL render completed steps with a green checkmark icon and full-opacity text
2. THE Research_Checklist SHALL render in-progress steps with an amber/orange spinning or pulsing icon and full-opacity text
3. THE Research_Checklist SHALL render not-started steps with a grey circle outline icon and reduced-opacity text (0.5 opacity)
4. WHEN a checklist step transitions from not-started to in-progress, THE Research_Checklist SHALL update the icon and text styling without a full page reload

### Requirement 10: Add Inline Progress Indicator to Research Checklist

**User Story:** As a trader monitoring research completeness, I want a progress bar displayed inline with the checklist, so that overall completion is visible at a glance without counting individual items.

#### Acceptance Criteria

1. THE Research_Checklist SHALL display a linear progress bar above the checklist items showing the ratio of completed steps to total steps
2. THE Research_Checklist SHALL display the completion count as text adjacent to the progress bar in the format "X of Y completed"
3. WHEN all steps are completed, THE Research_Checklist SHALL render the progress bar at 100% with a success colour (green)

### Requirement 11: Add Step Descriptions to Research Checklist

**User Story:** As a new user unfamiliar with the research pipeline, I want tooltip descriptions on each checklist step, so that I understand what each research phase involves without consulting external documentation.

#### Acceptance Criteria

1. WHEN the user hovers over a Research_Checklist step label, THE Research_Checklist SHALL display a tooltip containing a one-sentence description of what that research step validates
2. THE Research_Checklist SHALL provide descriptions for all 9 checklist steps (Initial Backtest, Monte Carlo Robustness, Walk-Forward Validation, Regime Sensitivity, Execution Realism Impact, Parameter Surface, Final Held-Out Test, Prop Firm Evaluation, CPCV Overfitting Assessment)

### Requirement 12: Improve Navigation Sidebar Hierarchy

**User Story:** As a user navigating the application, I want clear visual distinction between top-level sections and their children, so that the information architecture is immediately apparent.

#### Acceptance Criteria

1. THE NavMenu SHALL render top-level section headers (DASHBOARD, STRATEGIES, RESEARCH, BACKTESTS, PROP FIRM LAB, DATA, SETTINGS) with distinct typography (uppercase, smaller font, muted colour) that is visually differentiated from child navigation links
2. THE NavMenu SHALL render child navigation links with standard body typography and left indentation of at least 16 pixels from the section header
3. THE NavMenu SHALL render expandable group titles (STRATEGIES, RESEARCH, DATA) with a visual expand/collapse indicator (chevron icon) that rotates on state change

### Requirement 13: Make Version Selector Visually Interactive

**User Story:** As a user selecting strategy versions, I want the version selector to look like an interactive control, so that I recognise it as clickable without guessing.

#### Acceptance Criteria

1. WHEN a Version_Selector is rendered, THE Version_Selector SHALL display with a visible border, background colour distinct from surrounding text, and a dropdown indicator icon
2. WHEN the user hovers over the Version_Selector, THE Version_Selector SHALL display a hover state (background colour change or border highlight) indicating interactivity
