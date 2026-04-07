# TradingResearchEngine — UI Planning Specification

## 1. UI Goals

- Present backtest results, research workflow outputs, and prop-firm evaluations in a clear, interactive format
- Let the user create, edit, clone, and compare strategy configurations without touching JSON files
- Support configuring and launching backtests, parameter sweeps, Monte Carlo simulations, walk-forward analyses, and prop-firm evaluations from the UI
- Make scenario comparison a first-class workflow — not an afterthought
- Keep the CLI fully relevant for fast testing, scripting, and batch usage
- Sit cleanly on top of the existing Application layer — the UI is a presentation concern, not a rewrite
- Target Blazor Server for V1 (single-user, local, server-side rendering)
- Preserve the option to move to Blazor WASM or another frontend later

## 2. Architecture Fit

### What stays in CLI

- Fast single-run execution (`--scenario file.json`)
- Batch scripting (loop over scenarios, pipe results)
- Interactive scenario builder for quick ad-hoc runs
- Markdown report generation (`--output`)
- All CLI capabilities remain unchanged; the UI is additive

### What the UI consumes

The UI calls the same Application-layer services the CLI and API use:

- `RunScenarioUseCase` — single backtest
- `ParameterSweepWorkflow` — parameter grid search
- `MonteCarloWorkflow` — trade resampling simulation
- `WalkForwardWorkflow` — in-sample/out-of-sample validation
- `VarianceTestingWorkflow` — Conservative/Base/Strong presets
- `ParameterPerturbationWorkflow` — parameter jitter analysis
- `RandomizedOosWorkflow` — randomized out-of-sample
- `BenchmarkComparisonWorkflow` — strategy vs buy-and-hold
- `ScenarioComparisonUseCase` — side-by-side result comparison
- `PropFirmEvaluator` — challenge/instant-funding economics
- `PropFirmVarianceWorkflow` — prop-firm variance presets
- `IRepository<BacktestResult>` — saved results CRUD
- `StrategyRegistry` — available strategy names

The Blazor Server host injects these services directly via DI — no HTTP round-trip needed for V1. The API layer remains available for future decoupled clients.

### Avoiding UI-engine coupling

- The UI never constructs `BacktestEngine` directly — always goes through `RunScenarioUseCase`
- The UI never accesses `IDataProvider` or `IEventQueue` — those are engine internals
- All UI state is derived from `BacktestResult`, `ScenarioConfig`, and research workflow result types
- The UI builds `ScenarioConfig` objects and passes them to use cases — same contract as CLI and API
- Long-running jobs use `CancellationToken` propagation and progress reporting via injected services, not SignalR-specific patterns

### New Blazor Server project

```
src/TradingResearchEngine.Web/          ← Blazor Server host
  Program.cs                            ← DI wiring (same as CLI/API) + MudBlazor registration
  Components/
    Layout/                             ← MainLayout, NavMenu
    Pages/                              ← Routed Razor pages
    App.razor                           ← Root component
    Routes.razor                        ← Router
    _Imports.razor                      ← Global usings
  wwwroot/                              ← Static assets
```

Component library: MudBlazor (as recommended in section 12).

Dependency: `Web → Application + Infrastructure` (same as Cli and Api).

### Future multi-user note

If multi-user is added later, the main changes would be:
- Add authentication middleware
- Scope `IRepository` per user/workspace
- Add a job queue service (replacing in-process `Task` execution)
- Move from direct DI injection to API calls if the UI is decoupled


## 3. Navigation Model

The UI uses a left sidebar navigation with these top-level areas:

```
Dashboard
Strategies
  └─ Strategy List
  └─ Strategy Detail / Editor
Backtests
  └─ New Run
  └─ Run History
  └─ Result Detail
Research
  └─ Parameter Sweep
  └─ Monte Carlo
  └─ Walk-Forward
  └─ Variance Testing
  └─ Parameter Perturbation
  └─ Randomized OOS
  └─ Benchmark Comparison
Prop-Firm
  └─ Challenge Evaluator
  └─ Instant Funding Evaluator
  └─ Variance Presets
  └─ Rule Set Editor
Compare
  └─ Select Runs
  └─ Side-by-Side View
Data
  └─ Data Files
  └─ Import CSV
Settings
  └─ Risk Defaults
  └─ Slippage / Commission
  └─ Reporting Options
  └─ Repository Path
```

Breadcrumbs at the top of each page show the navigation path. The sidebar collapses on narrow viewports.


## 4. Screen Concepts

### 4.1 Dashboard

Purpose: At-a-glance overview of recent activity and key results.

Components:
- Recent runs table (last 10): ScenarioId, Strategy, Status, Sharpe, EndEquity, Date
- Quick-launch buttons: New Backtest, New Sweep, New Monte Carlo
- Summary cards: total runs, best Sharpe, worst drawdown, average expectancy across saved results
- Mini equity curve sparkline for the most recent completed run

User actions: Click a run to open its detail page. Click quick-launch to start a new workflow.

### 4.2 Strategy List

Purpose: Browse, search, and manage strategy configurations.

Components:
- Table: strategy name, type (from StrategyRegistry), parameter summary, last run date, best Sharpe
- Filters: by strategy type, by date range, by performance threshold
- Actions per row: Edit, Clone, Delete, Run Now

User actions: Create new strategy config, clone an existing one, open detail/editor.

### 4.3 Strategy Detail / Editor

Purpose: View and edit a strategy's parameters and run configuration.

Components:
- Strategy type selector (dropdown from StrategyRegistry.KnownNames)
- Dynamic parameter form: renders input fields based on the strategy's constructor parameters (name, type, default value)
- Execution assumptions panel: slippage model, commission model, initial cash, risk-free rate
- Data source panel: provider type, file path, symbol, interval, date range
- Validation feedback: inline errors for invalid fields before allowing run
- Action buttons: Save, Run, Clone, Export JSON

Data shown: all ScenarioConfig fields in an editable form.

### 4.4 Backtest — New Run

Purpose: Configure and launch a single backtest.

Components:
- Strategy selector (links to saved strategy configs or create inline)
- Data source configuration
- Execution assumptions (slippage, commission, initial cash)
- Risk parameters (max exposure %)
- Random seed toggle
- Run button with cancel capability
- Progress indicator during execution

User actions: Configure, run, view result when complete.

### 4.5 Backtest — Run History

Purpose: Browse all saved backtest results.

Components:
- Sortable/filterable table: RunId, ScenarioId, Strategy, Status, Sharpe, Calmar, MaxDD, WinRate, Expectancy, Trades, EndEquity, Duration, Date
- Filters: by strategy, by status, by date range, by metric thresholds
- Bulk actions: compare selected, delete selected, export selected
- Pagination for large result sets

User actions: Click row to open detail. Select multiple rows for comparison.

### 4.6 Backtest — Result Detail

Purpose: Deep-dive into a single backtest result.

Components:
- Summary card: all metrics from BacktestResult in a grid layout
- Equity curve chart (line)
- Drawdown chart (area, inverted)
- Trade list table: entry/exit time, price, direction, P&L, holding period
- Trade distribution histogram (P&L buckets)
- Win/loss pie chart
- Monthly return heatmap (if enough data)
- Equity curve smoothness indicator (R² gauge)
- Action buttons: Run Monte Carlo on this result, Run Prop-Firm evaluation, Compare with another run, Export Markdown

### 4.7 Research — Parameter Sweep

Purpose: Configure and run a parameter grid search.

Components:
- Base scenario selector
- Parameter grid builder: add parameter name, define value list (e.g. FastPeriod: [5, 10, 15, 20])
- Parallelism setting
- Run button with progress (X of Y combinations complete)
- Results: ranked table by Sharpe, parameter sensitivity heatmap, 3D surface plot if 2 parameters

### 4.8 Research — Monte Carlo

Purpose: Run bootstrap resampling on a completed backtest.

Components:
- Source result selector (from saved results)
- Simulation count input (default 1000)
- Seed input (optional)
- Ruin threshold input
- Run button with progress (X of N simulations)
- Results: P10/P50/P90 equity cards, ruin probability gauge, end equity distribution histogram, max drawdown distribution, P90 consecutive loss/win streaks, percentile band chart overlaid on original equity curve

### 4.9 Research — Walk-Forward

Purpose: Validate strategy generalisation across time windows.

Components:
- Base scenario selector
- Window configuration: IS length, OOS length, step size, anchored toggle
- Run button with progress (window X of Y)
- Results: window table (IS Sharpe, OOS Sharpe, efficiency ratio per window), mean efficiency ratio card, IS vs OOS Sharpe bar chart per window

### 4.10 Research — Variance Testing

Purpose: Stress-test a strategy across Conservative/Base/Strong assumptions.

Components:
- Base scenario selector
- Preset display (Conservative, Base, Strong overrides shown)
- Optional user-defined preset editor
- Run button
- Results: side-by-side metrics table for each preset, overlaid equity curves

### 4.11 Research — Parameter Perturbation

Purpose: Detect curve-fitting by jittering parameters.

Components:
- Base scenario selector
- Run count, jitter %, seed inputs
- Run button with progress
- Results: mean/stddev Sharpe cards, Sharpe distribution histogram, worst/best Sharpe, expectancy distribution

### 4.12 Research — Randomized OOS

Purpose: Validate with randomly withheld data segments.

Components:
- Base scenario selector
- OOS fraction, iteration count, seed inputs
- Run button with progress
- Results: IS vs OOS Sharpe per iteration scatter plot, mean OOS Sharpe card, stddev card, efficiency ratio distribution

### 4.13 Research — Benchmark Comparison

Purpose: Compare strategy against buy-and-hold.

Components:
- Base scenario selector
- Initial cash input
- Run button
- Results: strategy vs benchmark equity curves overlaid, alpha/beta/IR/tracking error cards, return comparison bar chart

### 4.14 Prop-Firm — Challenge Evaluator

Purpose: Evaluate a backtest result against a firm's challenge rules.

Components:
- Source result selector
- Firm rule set selector (from saved JSON) or inline editor
- Challenge config inputs: pass rate, conversion rate, account fee, notional size, drawdown limits
- Evaluate button
- Results: pass/fail badge, violated rules list, challenge probability, rule compliance timeline (drawdown vs limit over time)

### 4.15 Prop-Firm — Instant Funding Evaluator

Purpose: Model instant-funding economics.

Components:
- Config inputs: funded probability, account fee, notional, gross monthly return, payout split, friction factor, expected months
- Firm rule set selector
- Calculate button
- Results: monthly payout expectancy, lifetime EV, breakeven months, payout timeline chart

### 4.16 Prop-Firm — Variance Presets

Purpose: Run Conservative/Base/Strong prop-firm economics.

Components:
- Base instant funding config
- Optional user-defined preset
- Run button
- Results: side-by-side table of all presets (payout, EV, breakeven), bar chart comparison

### 4.17 Compare — Side-by-Side

Purpose: Compare 2+ saved backtest results.

Components:
- Result selector (multi-select from history, minimum 2)
- Compare button
- Results: ComparisonReport table with all metrics, best-by-Sharpe and best-by-drawdown badges, overlaid equity curves, overlaid drawdown charts, metric radar chart

### 4.18 Data — Data Files

Purpose: Manage CSV data files.

Components:
- File list table: filename, rows, date range, symbol
- Upload/import button
- Preview first N rows
- Validate schema (bar vs tick headers)

### 4.19 Settings

Purpose: Configure global defaults.

Components:
- Risk defaults: max exposure %
- Slippage/commission model defaults
- Reporting: decimal places
- Repository: base directory path
- All bound to IOptions<T> configuration sections


## 5. Chart Recommendations

| Chart Type | When to Use | Data Source |
|---|---|---|
| Line chart (equity curve) | Every result detail page; overlaid in comparisons | `BacktestResult.EquityCurve` |
| Area chart inverted (drawdown) | Result detail; shows underwater periods | Derived from equity curve peak-to-trough |
| Monthly return heatmap | Result detail when data spans multiple months | Derived from equity curve monthly deltas |
| Histogram (trade P&L distribution) | Result detail; shows skew and outliers | `BacktestResult.Trades[].NetPnl` |
| Pie/donut (win/loss breakdown) | Result detail summary | WinRate, 1-WinRate |
| Rolling line (rolling Sharpe/DD) | Result detail advanced tab | Computed from sliding window over trades |
| Heatmap (parameter sweep) | Sweep results; 2-param grid shows Sharpe by cell | `SweepResult.Results` mapped to param combos |
| 3D surface (parameter sweep) | Sweep results; 2-param grid with Sharpe as Z-axis | Same as heatmap, rendered as surface |
| Percentile band chart (Monte Carlo) | MC results; P10/P50/P90 bands over trade sequence | `MonteCarloResult.EndEquityDistribution` |
| Histogram (MC end equity) | MC results; shows outcome distribution shape | `MonteCarloResult.EndEquityDistribution` |
| Gauge (ruin probability) | MC results; single-value risk indicator | `MonteCarloResult.RuinProbability` |
| Grouped bar (walk-forward IS vs OOS) | WFA results; one group per window | `WalkForwardWindow.InSampleResult.SharpeRatio` vs `OutOfSampleResult.SharpeRatio` |
| Funnel (prop-firm pass/fail) | Prop-firm challenge; shows conversion stages | PassRate → Conversion → Funded |
| Bar chart (payout comparison) | Prop-firm variance; presets side by side | `PropFirmScenarioResult` per preset |
| Timeline (rule breach) | Prop-firm challenge; drawdown vs limit over time | Equity curve vs MaxTotalDrawdownPercent line |
| Radar chart (metric comparison) | Compare screen; multi-metric shape per run | Sharpe, Calmar, WinRate, PF, Smoothness per result |
| Scatter plot (randomized OOS) | Randomized OOS; IS Sharpe vs OOS Sharpe per iteration | `RandomizedOosIteration` pairs |
| Box plot (perturbation) | Parameter perturbation; Sharpe distribution | `PerturbationResult.Results[].SharpeRatio` |


## 6. Strategy Editing UX

### Strategy list view
- Sortable table of saved ScenarioConfigs
- Columns: name, strategy type, symbol, last modified, best Sharpe from linked runs
- Quick actions: Edit, Clone, Delete, Run

### Strategy detail view
- Read-only summary of all config fields
- Linked runs section: table of BacktestResults that used this config
- One-click "Run Again" with current parameters

### Strategy parameter editor
- Strategy type dropdown populated from `StrategyRegistry.KnownNames`
- On type change, the parameter form regenerates based on the selected strategy's constructor parameters (name, type, default)
- Numeric inputs with validation (min/max where applicable)
- Enum inputs as dropdowns (e.g. ReplayMode)
- Dictionary inputs as key-value pair editors (for DataProviderOptions, RiskParameters)

### Validation feedback
- Inline field-level errors (red border + message below field)
- Form-level validation summary at top
- Mirrors the same validation logic as `RunScenarioUseCase.Validate`
- Cannot submit/run until all required fields are valid

### Versioning / history
- V1: no formal versioning. Each save overwrites the config.
- Future: add a `Version` field to ScenarioConfig and store previous versions as separate JSON files. Show a version history dropdown on the detail page.

### Cloning
- "Clone" button creates a copy with a new ScenarioId (appends "-copy" or increments a counter)
- Opens the editor with all fields pre-filled from the source
- User can modify and save as a new config

### Comparing variants
- Select 2+ configs from the list → "Compare Configs" button
- Shows a diff table highlighting parameter differences
- One-click "Run Both and Compare Results"

### Separating strategy definition from run configuration
- Strategy definition = strategy type + strategy parameters (what the strategy does)
- Run configuration = data source + execution assumptions + risk parameters + research workflow (how the strategy is tested)
- The UI groups these into separate panels on the editor page
- A strategy definition can be reused across multiple run configurations


## 7. Backtest Execution UX

### Create run workflow
1. User navigates to Backtests → New Run
2. Selects a saved strategy config OR creates one inline
3. Reviews/adjusts execution assumptions (slippage, commission, initial cash)
4. Reviews/adjusts data source (symbol, interval, date range, CSV file)
5. Optionally sets a random seed for deterministic replay
6. Clicks "Run Backtest"

### Selecting data / symbol / timeframe
- Data provider type dropdown (csv, http)
- For CSV: file picker showing available files from the Data section
- Symbol text input (auto-suggest from previously used symbols)
- Interval dropdown (1D, 1H, 5m, etc.)
- Date range picker (from/to)

### Choosing strategy + parameters
- Strategy type dropdown
- Dynamic parameter form based on selected type
- "Load from saved config" button to pre-fill from a saved ScenarioConfig

### Execution assumptions
- Slippage model dropdown (Zero, FixedSpread) with model-specific parameter inputs
- Commission model dropdown (Zero, PerTrade, PerShare) with model-specific parameter inputs
- Initial cash input
- Annual risk-free rate input
- Max exposure % input

### Starting a test
- "Run Backtest" button disabled until validation passes
- On click: button changes to "Running..." with a spinner
- Cancel button appears alongside
- Progress text: "Processing bar 142 of 500..."

### Run status
- In-progress: spinner + progress text + cancel button
- Completed: green badge, auto-navigates to result detail (or shows inline summary)
- Failed: red badge with error message
- Cancelled: yellow badge

### Viewing finished results
- Auto-save to repository on completion
- Navigate to Result Detail page (section 4.6)
- Or stay on the run page with an inline summary + "View Full Results" link

### Saving / reloading scenarios
- "Save Config" button persists the ScenarioConfig to `IRepository<ScenarioConfig>`
- "Load Config" dropdown lists saved configs
- "Export JSON" downloads the ScenarioConfig as a .json file (compatible with CLI `--scenario`)

### Exporting markdown reports
- "Export Report" button on any result detail page
- Generates Markdown via `MarkdownReporter.RenderToMarkdown`
- Downloads as .md file or copies to clipboard


## 8. Prop-Firm Module UX

### Selecting a firm rule set
- Dropdown of saved FirmRuleSet JSON files
- "Create New" opens an inline editor with all required fields
- Validation via `FirmRuleSetValidator` before allowing evaluation

### Challenge vs instant funding flows
- Two separate pages under the Prop-Firm nav section
- Challenge flow: select a BacktestResult → configure ChallengeConfig → evaluate → see pass/fail + economics
- Instant funding flow: configure InstantFundingConfig → calculate → see payout expectancy + breakeven

### Payout assumptions
- All inputs from InstantFundingConfig exposed as form fields
- Gross monthly return %, payout split %, friction factor, expected payout months
- Real-time preview: as user adjusts inputs, the monthly payout and breakeven recalculate instantly (client-side or fast server round-trip via Blazor Server)

### Pass-rate assumptions
- PassRatePercent and PassToFundedConversionPercent as slider inputs (0-100%)
- Challenge probability computed and displayed live

### Variance presets vs user-defined
- Toggle between "Standard Presets" (Conservative/Base/Strong) and "Custom"
- Standard presets show their multipliers read-only
- Custom preset shows editable fields for gross return, friction, pass rate overrides
- "Run All" button executes all selected presets

### Linking prop-firm tests to backtest outputs
- The challenge evaluator requires a BacktestResult as input
- "Select Result" dropdown lists saved results with key metrics shown
- Or "Run Backtest First" link navigates to the New Run page
- After evaluation, the prop-firm result is linked to the source BacktestResult by RunId

### Showing profitability, breakeven, and rule compliance
- Summary cards: MonthlyPayoutExpectancy, LifetimeEV, BreakevenMonths, ChallengeProbability
- Rule compliance table: each rule from FirmRuleSet with pass/fail status and actual vs limit values
- Drawdown timeline chart: equity curve with MaxDailyDrawdown and MaxTotalDrawdown limit lines overlaid
- Payout timeline chart: cumulative payout over months with breakeven point marked


## 9. Comparison Workflows

### Strategy A vs Strategy B
- Select 2 results from history (different strategies, same data)
- Side-by-side metrics table
- Overlaid equity curves
- Radar chart comparing Sharpe, Calmar, WinRate, PF, Smoothness, Expectancy

### Parameter set A vs B
- Select 2 results from the same strategy with different parameters
- Highlight which parameters differ
- Side-by-side metrics + overlaid equity curves

### Same strategy across symbols / timeframes
- Select results from the same strategy run on different symbols or intervals
- Grouped bar chart: one group per symbol, bars for Sharpe, MaxDD, WinRate
- Table with symbol as row key

### Normal backtest vs prop-firm constrained
- Select a standard BacktestResult and its linked PropFirm evaluation
- Show the standard metrics alongside the prop-firm rule compliance
- Highlight where the strategy would pass/fail under firm constraints
- Drawdown chart with firm limit lines overlaid

### Baseline vs Monte Carlo output
- Select a BacktestResult and its linked MonteCarloResult
- Show the original equity curve with P10/P50/P90 percentile bands
- Cards: original end equity vs P10/P50/P90 end equity
- Ruin probability gauge

### Multiple saved runs
- Multi-select from history (2-10 runs)
- ComparisonReport table with all metrics
- Best-by badges (Sharpe, Drawdown, Expectancy, Smoothness)
- Overlaid equity curves (color-coded by run)
- Export comparison as Markdown table


## 10. Phased Implementation Roadmap

### Phase 1: Foundation (current + this document)
- ~~Complete UI planning specification (this document)~~ ✓
- Address blocking issues: progress reporting, result auto-save, ScenarioConfig persistence, strategy parameter metadata
- ~~Create the `TradingResearchEngine.Web` Blazor Server project with DI wiring~~ ✓
  - `Program.cs` wires `AddTradingResearchEngine`, `AddTradingResearchEngineInfrastructure`, `AddStrategyAssembly`, `AddMudServices`, and interactive server components
- Establish the component library foundation (layout, nav, shared components)
- No functional screens yet — just the shell

### Phase 2: Dashboard + Results Viewer
- Dashboard page with recent runs, summary cards, quick-launch buttons
- Run History page with sortable/filterable table
- Result Detail page with all metrics, equity curve chart, drawdown chart, trade list
- Basic charting integration (equity curve line chart, drawdown area chart)
- Auto-save results on completion

### Phase 3: Strategy Editor + Run Configuration
- Strategy List page
- Strategy Detail / Editor page with dynamic parameter form
- ScenarioConfig persistence (save/load/delete)
- New Run page with full configuration workflow
- Run execution with progress indicator and cancel
- Export JSON and Markdown from UI

### Phase 4: Research Workflows
- Parameter Sweep page with grid builder and results heatmap
- Monte Carlo page with simulation config and distribution charts
- Walk-Forward page with window config and IS/OOS comparison
- Variance Testing page with preset comparison
- Parameter Perturbation and Randomized OOS pages
- Benchmark Comparison page

### Phase 5: Prop-Firm Module Screens
- Challenge Evaluator page
- Instant Funding Evaluator page
- Variance Presets page
- Rule Set Editor
- Drawdown timeline with firm limit overlays

### Phase 6: Comparison Tools + Advanced Charts
- Multi-run comparison page
- Radar charts, heatmaps, 3D surface plots
- Monthly return heatmap
- Rolling metrics charts
- Report export refinement (PDF option, styled Markdown)

### Phase 7: Polish + Data Management
- Data Files page (list, upload, preview, validate)
- Settings page
- Responsive layout refinements
- Keyboard shortcuts for power users
- Error handling and edge case UX


## 11. Blazor Component Strategy

### Reusable components
- `MetricCard` — single metric with label, value, optional trend indicator
- `MetricsGrid` — grid of MetricCards for a BacktestResult
- `EquityCurveChart` — line chart accepting `IReadOnlyList<EquityCurvePoint>`
- `DrawdownChart` — inverted area chart derived from equity curve
- `TradeTable` — sortable/paginated table of ClosedTrade records
- `ScenarioConfigForm` — dynamic form that renders fields from a ScenarioConfig
- `ParameterEditor` — dynamic key-value editor for strategy parameters
- `RunProgressIndicator` — spinner + progress text + cancel button
- `ResultSelector` — dropdown/search for selecting saved BacktestResults
- `ComparisonTable` — side-by-side metrics for multiple results

### Tables / grids
- Use a single `DataGrid<T>` component pattern with sorting, filtering, pagination
- Virtual scrolling for large trade lists (1000+ rows)
- Column visibility toggles for wide tables

### Chart components
- Wrap the chosen charting library in thin Blazor components
- Each chart component accepts strongly-typed data (not raw JSON)
- `ChartContainer` wrapper handles loading state, empty state, and resize

### State management
- Use scoped services for page-level state (current run, selected results)
- Use a singleton `RunHistoryService` that wraps `IRepository<BacktestResult>` with in-memory caching
- Use a singleton `ScenarioConfigService` that wraps `IRepository<ScenarioConfig>`
- Avoid Blazor cascading parameters for complex state — use injected services instead
- Use `StateHasChanged()` sparingly; prefer event-driven updates

### Long-running job handling
- `BacktestJobRunner` service (scoped per page) that:
  - Accepts a ScenarioConfig and workflow type
  - Runs the workflow on a background thread via `Task.Run`
  - Exposes `IsRunning`, `Progress`, `Result`, `Error` properties
  - Supports `CancellationToken` for cancel
  - Calls `InvokeAsync(StateHasChanged)` on the Blazor component when progress updates
- For Blazor Server, this works naturally since the service lives on the server
- Future WASM migration would replace this with API polling or SignalR

### Notifications / progress UX
- Toast notifications for: run completed, run failed, result saved, export complete
- Progress bar component for long-running workflows (sweep, MC, WFA)
- Non-blocking: user can navigate away and return; the job continues in the background
- Job status badge in the sidebar showing active runs

### Keeping large result sets responsive
- Paginate trade lists and equity curve points in tables
- Charts: downsample equity curves with 1000+ points to ~500 points for rendering
- Lazy-load trade details on expand/click
- Virtual scrolling for history tables with 100+ results


## 12. Charting Library Evaluation

Do not lock to a specific library yet. Evaluate these at the start of Phase 2:

| Library | Pros | Cons | Notes |
|---|---|---|---|
| Radzen Blazor Charts | Free, native Blazor, good docs | Limited chart types, no 3D | Good for V1 basics |
| ApexCharts.Blazor | Rich chart types, responsive, free | JS interop dependency | Strong candidate for line/bar/heatmap |
| Plotly.Blazor | Full Plotly.js power, 3D support | Heavy JS bundle, complex API | Best for advanced charts (surface, heatmap) |
| Syncfusion Blazor | Enterprise-grade, all chart types | Commercial license (free community) | Overkill for V1 single-user |
| MudBlazor Charts | Part of MudBlazor component library | Basic chart types only | Good if using MudBlazor for layout |
| Lightweight Charts (TradingView) | Purpose-built for financial data | JS interop, no native Blazor wrapper | Ideal for equity curves if wrapped |

Recommendation for V1: Start with ApexCharts.Blazor for most charts (line, bar, area, histogram, pie, heatmap, radar). Evaluate Plotly.Blazor for 3D surface plots in Phase 6. Consider TradingView Lightweight Charts for equity curve rendering if ApexCharts doesn't feel right for financial time series.

For the overall component library (layout, forms, tables, buttons), evaluate:
- MudBlazor (most popular, good defaults, MIT license)
- Radzen (free, comprehensive, slightly heavier)
- FluentUI Blazor (Microsoft's design system, newer)

Recommendation: MudBlazor for V1. It covers layout, navigation, forms, tables, dialogs, and basic charts. Supplement with ApexCharts for advanced charting. MudBlazor has been integrated into the Web project (`MudBlazor` NuGet package, `AddMudServices()` in `Program.cs`).


## 13. Risks and UX Pitfalls

### Trying to put too much into one screen
- Risk: the Result Detail page has 20+ metrics, equity curve, drawdown, trade list, distribution, heatmap
- Mitigation: use tabs or collapsible sections. Default view shows summary cards + equity curve. Advanced metrics, trade list, and distribution charts are in separate tabs.

### Confusing strategy definition with execution configuration
- Risk: user doesn't understand why changing "initial cash" doesn't change the strategy
- Mitigation: visually separate "Strategy" panel (type + parameters) from "Execution" panel (data, slippage, commission, cash). Use different background colors or card borders.

### Overloading charts
- Risk: overlaying 10 equity curves on one chart becomes unreadable
- Mitigation: limit comparison to 5 runs max on a single chart. Use color coding with a legend. Allow toggling individual series on/off.

### Blocking the UI during long-running tests
- Risk: Monte Carlo with 10,000 sims or a 50-combination sweep freezes the page
- Mitigation: always run workflows on a background thread. Show progress. Allow cancel. Allow navigation away without losing the job.

### Losing CLI parity
- Risk: features added to the UI but not exposed via CLI, or vice versa
- Mitigation: both CLI and UI consume the same Application-layer services. New workflows are added to Application first, then surfaced in both CLI and UI. The UI never contains business logic.

### Mixing prop-firm concerns into generic backtest screens
- Risk: prop-firm fields (pass rate, payout split) appearing on the standard backtest form
- Mitigation: prop-firm is a separate nav section. The only connection point is "Evaluate this result against a firm" — a link from the result detail page to the prop-firm evaluator, pre-populated with the selected result.

### Large result sets degrading performance
- Risk: 10,000-point equity curves or 500+ saved results causing slow rendering
- Mitigation: virtual scrolling for tables, chart downsampling, pagination, lazy loading of trade details.

### JSON serialization fragility
- Risk: adding fields to BacktestResult breaks deserialization of old saved results
- Mitigation: use `JsonSerializerOptions { DefaultIgnoreCondition = WhenWritingNull }` and make new fields nullable with defaults. Old JSON files missing new fields deserialize with null/default values.

### SignalR connection drops (Blazor Server)
- Risk: long-running backtest loses connection, user sees "reconnecting" overlay
- Mitigation: Blazor Server's built-in reconnection handles transient drops. For truly long jobs (minutes), consider persisting job state so the UI can recover on reconnect. V1 can accept the limitation since it's single-user local.


## 14. Summary and Recommendations

### Key architectural decisions
- Blazor Server for V1, single-user, local deployment
- UI injects Application-layer services directly via DI (no HTTP for V1)
- CLI remains first-class; UI is additive
- Prop-firm module stays bounded — separate nav section, linked by result selection

### Pre-UI blocking work
1. ~~Add `IProgress<T>` support to workflow interfaces for progress reporting~~ — Done. `ProgressUpdate` record added to `Core.Engine`; `IResearchWorkflow` now has an overload accepting `IProgress<ProgressUpdate>?`.
2. Auto-save BacktestResults on completion in RunScenarioUseCase
3. Add `IRepository<ScenarioConfig>` for scenario persistence
4. Add strategy parameter metadata to StrategyRegistry (parameter names, types, defaults)

### Recommended V1 tech stack
- Blazor Server (.NET 8)
- MudBlazor for layout, forms, tables, navigation
- ApexCharts.Blazor for charting
- Existing Application + Infrastructure layers unchanged

### Implementation order
Phase 1 → Phase 2 → Phase 3 → Phase 4 → Phase 5 → Phase 6 → Phase 7

Each phase is independently deployable and testable. No phase depends on a later phase.

### What not to do
- Do not rewrite the engine to accommodate the UI
- Do not add UI-specific logic to Application or Core
- Do not skip the progress reporting infrastructure — it's critical for UX
- Do not try to build all chart types in Phase 2 — start with equity curve and drawdown only
- Do not build the comparison screen before the result detail screen is solid
test