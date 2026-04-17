# Requirements Document

## Introduction

Research Page Routing & UX Fixes (V7) generalizes the Monte Carlo page pattern — the only fully working research page — across all remaining research pages. The feature wires URL query parameters into every research page, adds missing launch buttons and study types, introduces a reusable StrategyVersionPicker component, persists and displays study results in StudyDetail, makes ResearchExplorer rows clickable, and replaces hardcoded timeframe options with the canonical TimeframeOptions list. V7 also adds a direct `GetVersionAsync` lookup to IStrategyRepository to eliminate O(n×m) full scans, wires BackgroundStudyService dispatch for BenchmarkComparison/Variance/RandomisedOos study types, reads BenchmarkExcessSharpe from real persisted study data instead of a hardcoded approximation, and ensures the Strategy Library hides retired strategies by default with a "Show Retired" toggle and counter.

## Glossary

- **Research_Page**: A Blazor page under `/research/` that runs a specific research workflow (MonteCarlo, WalkForward, Sweep, Perturbation, Benchmark, Variance).
- **ScenarioConfigEditor**: A shared Blazor component that builds a `ScenarioConfig` from user input; exposes `LoadFromConfig(ScenarioConfig)` and `BuildConfig()` methods.
- **StrategyVersionPicker**: A new Blazor component that lets the user select a strategy and version from dropdowns, emitting the selection including the latest backtest run.
- **StrategyDetail_Page**: The Blazor page at `/strategies/{StrategyId}` that shows strategy metadata, runs, research studies, and launch buttons.
- **StudyDetail_Page**: The Blazor page at `/research/study/{StudyId}` that displays metadata and results for a completed study.
- **ResearchExplorer_Page**: The Blazor page at `/research/explorer` that lists all study records in a table.
- **IStrategyRepository**: Application-layer persistence interface for strategy identities and versions.
- **IRepository_BacktestResult**: Core persistence interface (`IRepository<BacktestResult>`) for backtest results.
- **IStudyRepository**: Application-layer persistence interface for study records.
- **StudyType_Enum**: The `StudyType` enum in `TradingResearchEngine.Application.Research` that identifies research workflow types.
- **StudyRecord**: The domain record representing a research workflow execution, linked to a strategy version.
- **TimeframeOptions**: A static class providing the canonical list of all 8 intraday timeframe options (M1 through D1).
- **Query_Parameter**: A URL query string key-value pair passed via navigation (e.g., `?strategyId=abc&versionId=def`).
- **SupplyParameterFromQuery**: A Blazor attribute that binds a component parameter from the URL query string.
- **GetVersionAsync**: A method on IStrategyRepository that looks up a StrategyVersion directly by its `strategyVersionId`, avoiding O(n×m) full scans.
- **BackgroundStudyService**: An Application-layer singleton service that manages background execution of long-running studies and dispatches study types to their respective workflow classes.
- **BenchmarkExcessSharpe**: A metric chip on StrategyDetail_Page showing the excess Sharpe ratio of the strategy versus a buy-and-hold baseline, sourced from a completed BenchmarkComparison study's `ResultJson`. The underlying property on `BenchmarkComparisonResult` is `ExcessReturn` (defined as strategy Sharpe minus benchmark Sharpe).
- **StrategyLibrary_Page**: The Blazor page at `/strategies/library` that lists all strategies as cards with filtering, search, and retirement management.
- **BarsPerYearDefaults**: A static class in `TradingResearchEngine.Core.Configuration` providing canonical bars-per-year constants for all 8 intraday timeframes (M1 through D1).
- **RandomisedOos**: A study type representing randomised out-of-sample sampling analysis, dispatched to `RandomizedOosWorkflow`.

## Requirements

### Requirement 1: WalkForward Page Query Parameter Wiring

**User Story:** As a researcher, I want the WalkForward page to accept `strategyId` and `versionId` query parameters from StrategyDetail navigation, so that the ScenarioConfigEditor is pre-populated with the correct strategy version configuration without manual entry.

#### Acceptance Criteria

1. WHEN the WalkForward_Page receives a `versionId` Query_Parameter, THE WalkForward_Page SHALL look up the strategy version via IStrategyRepository, load the version's `BaseScenarioConfig`, and call `ScenarioConfigEditor.LoadFromConfig` with that config.
2. WHEN the WalkForward_Page receives a `strategyId` Query_Parameter without a `versionId`, THE WalkForward_Page SHALL find the latest version for that strategy via IStrategyRepository and pre-populate ScenarioConfigEditor with the latest version's `BaseScenarioConfig`.
3. WHEN the WalkForward_Page receives neither `strategyId` nor `versionId` Query_Parameters, THE WalkForward_Page SHALL display the ScenarioConfigEditor in its default empty state.
4. IF the provided `versionId` does not resolve to a valid strategy version, THEN THE WalkForward_Page SHALL display a warning notification via ISnackbar and leave ScenarioConfigEditor in its default state.

### Requirement 2: Sweep Page Query Parameter Wiring

**User Story:** As a researcher, I want the Sweep page to accept `strategyId` and `versionId` query parameters, so that the ScenarioConfigEditor is pre-populated with the correct configuration when launched from StrategyDetail.

#### Acceptance Criteria

1. WHEN the Sweep_Page receives a `versionId` Query_Parameter, THE Sweep_Page SHALL look up the strategy version via IStrategyRepository, load the version's `BaseScenarioConfig`, and call `ScenarioConfigEditor.LoadFromConfig` with that config.
2. WHEN the Sweep_Page receives a `strategyId` Query_Parameter without a `versionId`, THE Sweep_Page SHALL find the latest version for that strategy via IStrategyRepository and pre-populate ScenarioConfigEditor with the latest version's `BaseScenarioConfig`.
3. WHEN the Sweep_Page receives neither `strategyId` nor `versionId` Query_Parameters, THE Sweep_Page SHALL display the ScenarioConfigEditor in its default empty state.
4. IF the provided `versionId` does not resolve to a valid strategy version, THEN THE Sweep_Page SHALL display a warning notification via ISnackbar and leave ScenarioConfigEditor in its default state.

### Requirement 3: Perturbation Page Query Parameter Wiring

**User Story:** As a researcher, I want the Perturbation page to accept a `result` query parameter, so that the ScenarioConfigEditor is pre-populated from the referenced backtest result's config when launched from StrategyDetail.

#### Acceptance Criteria

1. WHEN the Perturbation_Page receives a `result` Query_Parameter, THE Perturbation_Page SHALL load the BacktestResult via IRepository_BacktestResult and call `ScenarioConfigEditor.LoadFromConfig` with the result's `ScenarioConfig`.
2. WHEN the Perturbation_Page receives no `result` Query_Parameter, THE Perturbation_Page SHALL display the ScenarioConfigEditor in its default empty state.
3. IF the provided `result` ID does not resolve to a valid BacktestResult, THEN THE Perturbation_Page SHALL display a warning notification via ISnackbar and leave ScenarioConfigEditor in its default state.

### Requirement 4: Benchmark Page Query Parameter Wiring

**User Story:** As a researcher, I want the Benchmark page to accept `strategyId` and `versionId` query parameters, so that the StrategyVersionPicker is pre-populated and the ScenarioConfigEditor loads the correct configuration when launched from StrategyDetail.

#### Acceptance Criteria

1. WHEN the Benchmark_Page receives `strategyId` and `versionId` Query_Parameters, THE Benchmark_Page SHALL pass the values to StrategyVersionPicker as `PreselectedStrategyId` and `PreselectedVersionId`, which auto-selects the strategy and version and populates ScenarioConfigEditor with the version's `BaseScenarioConfig`.
2. WHEN the Benchmark_Page receives a `result` Query_Parameter, THE Benchmark_Page SHALL load the BacktestResult via IRepository_BacktestResult and call `ScenarioConfigEditor.LoadFromConfig` with the result's `ScenarioConfig`.
3. WHEN the Benchmark_Page receives no query parameters, THE Benchmark_Page SHALL display the StrategyVersionPicker and ScenarioConfigEditor in their default empty states.
4. IF the provided `versionId` does not resolve to a valid strategy version, THEN THE Benchmark_Page SHALL display a warning notification via ISnackbar and leave the editors in their default state.

### Requirement 5: Variance Page Query Parameter Wiring

**User Story:** As a researcher, I want the Variance page to accept `strategyId` and `versionId` query parameters, so that the StrategyVersionPicker is pre-populated and the ScenarioConfigEditor loads the correct configuration when launched from StrategyDetail.

#### Acceptance Criteria

1. WHEN the Variance_Page receives `strategyId` and `versionId` Query_Parameters, THE Variance_Page SHALL pass the values to StrategyVersionPicker as `PreselectedStrategyId` and `PreselectedVersionId`, which auto-selects the strategy and version and populates ScenarioConfigEditor with the version's `BaseScenarioConfig`.
2. WHEN the Variance_Page receives a `result` Query_Parameter, THE Variance_Page SHALL load the BacktestResult via IRepository_BacktestResult and call `ScenarioConfigEditor.LoadFromConfig` with the result's `ScenarioConfig`.
3. WHEN the Variance_Page receives no query parameters, THE Variance_Page SHALL display the StrategyVersionPicker and ScenarioConfigEditor in their default empty states.
4. IF the provided `versionId` does not resolve to a valid strategy version, THEN THE Variance_Page SHALL display a warning notification via ISnackbar and leave the editors in their default state.

### Requirement 6: Add BenchmarkComparison, Variance, and RandomisedOos to StudyType Enum

**User Story:** As a developer, I want BenchmarkComparison, Variance, and RandomisedOos values in the StudyType enum, so that StrategyDetail can map launch URLs, BackgroundStudyService can dispatch these study types, and the system can categorize them.

#### Acceptance Criteria

1. THE StudyType_Enum SHALL include a `BenchmarkComparison` value with a distinct integer value.
2. THE StudyType_Enum SHALL include a `Variance` value with a distinct integer value.
3. THE StudyType_Enum SHALL include a `RandomisedOos` value with a distinct integer value.
4. WHEN the StudyType_Enum is serialized to JSON, THE StudyType_Enum SHALL produce the string `"BenchmarkComparison"` for the BenchmarkComparison value, `"Variance"` for the Variance value, and `"RandomisedOos"` for the RandomisedOos value.
5. THE StudyType_Enum SHALL have no two values sharing the same underlying integer (all values are unique, excluding the existing `Cpcv` alias for `CombinatorialPurgedCV`).

### Requirement 7: Add Benchmark and Variance Launch Buttons to StrategyDetail

**User Story:** As a researcher, I want Benchmark and Variance launch buttons in the StrategyDetail Research tab, so that I can launch these studies directly from the strategy workspace.

#### Acceptance Criteria

1. WHEN a selected version exists, THE StrategyDetail_Page SHALL display a "Benchmark" launch button in the Research tab launch bar that navigates to `/research/benchmark?strategyId={strategyId}&versionId={selectedVersionId}`.
2. WHEN a selected version exists, THE StrategyDetail_Page SHALL display a "Variance" launch button in the Research tab launch bar that navigates to `/research/variance?strategyId={strategyId}&versionId={selectedVersionId}`.
3. WHEN the `GetStudyLaunchUrl` method receives `StudyType.BenchmarkComparison`, THE StrategyDetail_Page SHALL return the URL `/research/benchmark?strategyId={strategyId}&versionId={selectedVersionId}`.
4. WHEN the `GetStudyLaunchUrl` method receives `StudyType.Variance`, THE StrategyDetail_Page SHALL return the URL `/research/variance?strategyId={strategyId}&versionId={selectedVersionId}`.

### Requirement 8: StrategyVersionPicker Component

**User Story:** As a researcher, I want a reusable StrategyVersionPicker component that lets me select a strategy and version from dropdowns with optional URL-driven preselection, so that research pages have a consistent, user-friendly input path.

#### Acceptance Criteria

1. THE StrategyVersionPicker SHALL display a MudSelect dropdown populated with all strategies loaded from IStrategyRepository.
2. WHEN a strategy is selected, THE StrategyVersionPicker SHALL display a second MudSelect dropdown populated with all versions for that strategy.
3. WHEN a version is selected, THE StrategyVersionPicker SHALL emit an `OnSelectionChanged` event containing the selected StrategyIdentity, StrategyVersion, and the latest BacktestResult for that version (or null if no runs exist).
4. THE StrategyVersionPicker SHALL accept an `EventCallback<(StrategyIdentity Strategy, StrategyVersion Version, BacktestResult? LatestRun)>` parameter named `OnSelectionChanged`.
5. THE StrategyVersionPicker SHALL accept `PreselectedStrategyId` and `PreselectedVersionId` string parameters for URL-driven preselection.
6. WHEN `PreselectedStrategyId` is provided, THE StrategyVersionPicker SHALL auto-select that strategy and load its versions on initialization.
7. WHEN `PreselectedVersionId` is provided, THE StrategyVersionPicker SHALL auto-select that version and emit the `OnSelectionChanged` event on initialization.
8. THE StrategyVersionPicker SHALL filter out retired strategies (Stage == Retired) from the strategy dropdown.

### Requirement 9: Integrate StrategyVersionPicker into Research Pages

**User Story:** As a researcher, I want the StrategyVersionPicker as the primary input on all research pages, with ScenarioConfigEditor available as an expandable advanced section, so that I have a simple default path and a power-user fallback.

#### Acceptance Criteria

1. THE Perturbation_Page SHALL display the StrategyVersionPicker as the primary input control above the workflow options.
2. THE Benchmark_Page SHALL display the StrategyVersionPicker as the primary input control above the workflow options.
3. THE Variance_Page SHALL display the StrategyVersionPicker as the primary input control above the workflow options.
4. THE WalkForward_Page SHALL display the StrategyVersionPicker as the primary input control above the window configuration.
5. THE Sweep_Page SHALL display the StrategyVersionPicker as the primary input control above the parameter grid.
6. WHEN a version is selected via StrategyVersionPicker, THE Research_Page SHALL call `ScenarioConfigEditor.LoadFromConfig` with the selected version's `BaseScenarioConfig`.
7. THE Research_Page SHALL render the ScenarioConfigEditor inside a collapsible "Advanced / Manual Config" section using MudExpansionPanel, collapsed by default.

### Requirement 10: Persist Study Results in IStudyRepository

**User Story:** As a developer, I want study results persisted alongside the StudyRecord for all workflow types, so that StudyDetail can display results after the workflow completes and the BenchmarkExcessSharpe chip can read real data.

#### Acceptance Criteria

1. THE IStudyRepository SHALL expose a `SaveResultAsync(string studyId, string resultJson)` method that stores serialized result JSON for a study.
2. THE StudyRecord SHALL include a nullable `ResultJson` property of type `string`. Callers read results directly from `StudyRecord.ResultJson` after loading the study via `GetAsync`.
3. WHEN a research workflow completes, THE workflow orchestrator SHALL serialize the result to JSON and call `SaveResultAsync` with the study ID and result JSON.
4. THE workflow orchestrator SHALL persist results for all workflow types: MonteCarlo, WalkForward, AnchoredWalkForward, ParameterSweep, Sensitivity, Realism, RegimeSegmentation, BenchmarkComparison, Variance, RandomisedOos, and Cpcv.

### Requirement 11: Display Study Results in StudyDetail

**User Story:** As a researcher, I want StudyDetail to render the actual study results (charts and metrics) when available, so that I can review completed study outcomes without re-running the workflow.

#### Acceptance Criteria

1. WHEN a StudyRecord has a non-null `ResultJson`, THE StudyDetail_Page SHALL deserialize the result based on the study's `Type` field.
2. WHEN the study type is MonteCarlo, THE StudyDetail_Page SHALL render the `MonteCarloFanChart` and `EquityDistributionChart` components with the deserialized result data.
3. WHEN the study type is WalkForward, THE StudyDetail_Page SHALL render the `WalkForwardCompositeChart` component with the deserialized result data.
4. WHEN the study type is Sensitivity or ParameterSweep, THE StudyDetail_Page SHALL render the `ParameterSweepHeatmap` component with the deserialized result data.
5. WHEN the study type is Realism (Perturbation), THE StudyDetail_Page SHALL render metric cards showing MeanSharpe, StdDevSharpe, MeanExpectancy, WorstSharpe, and BestSharpe from the deserialized result data.
6. WHEN the `ResultJson` is null, THE StudyDetail_Page SHALL display only the existing metadata and interpretation sections without a results panel.
7. WHEN the study type is BenchmarkComparison, THE StudyDetail_Page SHALL render metric cards showing StrategyReturn, BenchmarkReturn, Alpha, Beta, InformationRatio, and TrackingError from the deserialized `BenchmarkComparisonResult`.
8. WHEN the study type is Cpcv, THE StudyDetail_Page SHALL render metric cards showing MedianOosSharpe, ProbabilityOfOverfitting, and PerformanceDegradation from the deserialized `CpcvResult`.
9. WHEN the study type is Variance, THE StudyDetail_Page SHALL render a variant table with columns PresetName, Sharpe, MaxDD, WinRate, Trades, and EndEquity from the deserialized variance result data.

### Requirement 12: ResearchExplorer Row Click Navigation

**User Story:** As a researcher, I want to click a row in the ResearchExplorer table to navigate to the study detail page, so that I can quickly drill into any study.

#### Acceptance Criteria

1. WHEN a row is clicked in the ResearchExplorer_Page MudTable, THE ResearchExplorer_Page SHALL navigate to `/research/study/{studyId}`.
2. THE ResearchExplorer_Page SHALL display an action column with a MudIconButton on each row that navigates to `/research/study/{studyId}`.

### Requirement 13: Execution Window Dialog Timeframe Options and BarsPerYear Auto-Population

**User Story:** As a researcher, I want the Edit Execution Window dialog in StrategyDetail to show all intraday timeframes from TimeframeOptions and auto-populate BarsPerYear on selection, so that I can select any supported timeframe and get the correct annualisation constant automatically.

#### Acceptance Criteria

1. THE StrategyDetail_Page Edit Execution Window dialog SHALL populate its timeframe MudSelect from `TimeframeOptions.All` instead of hardcoded values.
2. THE StrategyDetail_Page Edit Execution Window dialog SHALL display each timeframe option using its `Label` property as the display text and its `Value` property as the select value.
3. THE StrategyDetail_Page Edit Execution Window dialog SHALL include all 8 timeframes defined in TimeframeOptions: M1, M5, M15, M30, H1, H2, H4, D1.
4. WHEN a timeframe is selected in the Edit Execution Window dialog, THE StrategyDetail_Page SHALL auto-populate `BarsPerYear` from the corresponding `BarsPerYearDefaults` constant for the selected timeframe.
5. WHEN a timeframe is selected in `Step2DataExecutionWindow.razor`, THE Step2DataExecutionWindow SHALL auto-populate `BarsPerYear` from the corresponding `BarsPerYearDefaults` constant for the selected timeframe.

### Requirement 14: Add GetVersionAsync to IStrategyRepository

**User Story:** As a developer, I want a direct version lookup by ID on IStrategyRepository, so that callers can resolve a StrategyVersion without O(n×m) full scans across all strategies and versions.

#### Acceptance Criteria

1. THE IStrategyRepository SHALL expose a `GetVersionAsync(string strategyVersionId, CancellationToken ct)` method that returns a `StrategyVersion` or null if not found.
2. THE `JsonStrategyRepository` SHALL implement `GetVersionAsync` using a two-level directory scan: iterating strategy directories and checking for a matching version file by ID, without deserializing every version.
3. WHEN `GetVersionAsync` is called with a valid `strategyVersionId`, THE `JsonStrategyRepository` SHALL return the deserialized `StrategyVersion` from the matching file.
4. WHEN `GetVersionAsync` is called with a `strategyVersionId` that does not match any file, THE `JsonStrategyRepository` SHALL return null.
5. WHEN `GetVersionAsync` is called and the base directory does not exist, THE `JsonStrategyRepository` SHALL return null without throwing an exception.
6. THE `ResearchChecklistService` SHALL use `IStrategyRepository.GetVersionAsync` instead of listing all strategies and iterating all versions to resolve a `strategyVersionId`.
7. THE `StudyDetail_Page` SHALL use `IStrategyRepository.GetVersionAsync` instead of the O(n×m) nested loop to resolve the strategy name from a `strategyVersionId`.
8. THE `ResearchExplorer_Page` SHALL use `IStrategyRepository.GetVersionAsync` instead of the O(n×m) nested loop to resolve strategy names from `strategyVersionId` values.

### Requirement 15: BackgroundStudyService Dispatch for New Study Types

**User Story:** As a developer, I want BackgroundStudyService to dispatch BenchmarkComparison, Variance, and RandomisedOos study types to their respective workflow classes, so that these studies can be executed as background tasks.

#### Acceptance Criteria

1. WHEN BackgroundStudyService receives a study of type `StudyType.BenchmarkComparison`, THE BackgroundStudyService SHALL dispatch execution to `BenchmarkComparisonWorkflow`.
2. WHEN BackgroundStudyService receives a study of type `StudyType.Variance`, THE BackgroundStudyService SHALL dispatch execution to `VarianceTestingWorkflow`.
3. WHEN BackgroundStudyService receives a study of type `StudyType.RandomisedOos`, THE BackgroundStudyService SHALL dispatch execution to `RandomizedOosWorkflow`.
4. THE BackgroundStudyService SHALL follow the same dispatch pattern as existing study types: inject the workflow, call `RunAsync`, serialize the result to JSON, and call `SaveResultAsync`.

### Requirement 16: BenchmarkExcessSharpe Chip Wiring from Real Data

**User Story:** As a researcher, I want the BenchmarkExcessSharpe chip on StrategyDetail to read from a real completed BenchmarkComparison study result, so that the displayed excess Sharpe is accurate rather than a hardcoded approximation.

#### Acceptance Criteria

1. THE StrategyDetail_Page SHALL read the BenchmarkExcessSharpe value from the latest completed BenchmarkComparison study's `ResultJson`, deserializing `BenchmarkComparisonResult.ExcessReturn`.
2. WHEN loading BenchmarkExcessSharpe, THE StrategyDetail_Page SHALL query studies via `IStudyRepository.ListByVersionAsync` for the selected version, filter to `StudyType.BenchmarkComparison` with `StudyStatus.Completed` and non-null `ResultJson`, and select the most recent by `CreatedAt`.
3. WHEN no completed BenchmarkComparison study exists for the selected version, THE StrategyDetail_Page SHALL display the BenchmarkExcessSharpe chip as null with a tooltip reading "Run a Benchmark Comparison study to see excess Sharpe vs Buy & Hold".
4. THE StrategyDetail_Page SHALL NOT use `_latestRun.SharpeRatio` as a fallback approximation for BenchmarkExcessSharpe.

### Requirement 17: Strategy Library Retired Strategy Toggle and Counter

**User Story:** As a researcher, I want the Strategy Library to hide retired strategies by default with a toggle to show them and a counter of hidden strategies, so that my active workspace is uncluttered while retired strategies remain accessible.

#### Acceptance Criteria

1. THE StrategyLibrary_Page SHALL hide strategies with `Stage == DevelopmentStage.Retired` by default.
2. THE StrategyLibrary_Page SHALL display a "Show Retired" toggle in the page toolbar bound to a boolean state, defaulting to false.
3. WHEN the "Show Retired" toggle is enabled, THE StrategyLibrary_Page SHALL display all strategies including retired ones.
4. THE StrategyLibrary_Page SHALL display retired strategy cards with reduced opacity (0.5) and a "RETIRED" chip badge.
5. WHEN the "Show Retired" toggle is disabled and retired strategies exist, THE StrategyLibrary_Page SHALL display a counter in the toolbar showing how many retired strategies are hidden (e.g., "N retired hidden").
6. THE StrategyLibrary_Page retirement confirmation dialog SHALL include a MudTextField for an optional `RetirementNote`.
7. WHEN retirement is confirmed, THE StrategyLibrary_Page SHALL save the strategy with `Stage = DevelopmentStage.Retired` and the provided `RetirementNote` via `IStrategyRepository.SaveAsync`.
