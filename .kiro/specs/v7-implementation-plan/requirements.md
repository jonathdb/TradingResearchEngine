# Requirements Document

## Introduction

This document defines the requirements for the TradingResearchEngine V7 Implementation Plan — a 25-item improvement programme across five phases. The plan addresses broken functionality, navigation gaps, builder and engine quality, LLM-assisted strategy creation, and long-term architectural improvements. The TradingResearchEngine is a .NET 8 / C# 12 event-driven backtesting platform with a Blazor Server UI (MudBlazor), clean architecture (`Core ← Application ← Infrastructure ← Web/Api/Cli`), JSON file repositories with a SQLite index layer, and xUnit + FsCheck testing.

## Glossary

- **Builder**: The 5-step strategy creation wizard (`StrategyBuilder.razor`) that guides users through template selection, data configuration, parameter tuning, realism settings, and launch.
- **BuilderViewModel**: The mutable view model (`BuilderViewModel.cs`) that holds transient builder state in the Web layer and maps to immutable domain records on save.
- **ConfigDraft**: An immutable Application-layer record representing a persisted snapshot of in-progress builder state.
- **Dashboard**: The landing page (`Dashboard.razor`) showing strategy status strips, KPI tiles, recent runs, and robustness flags.
- **Fork**: A builder source mode where the user selects an existing `StrategyIdentity` and creates a new strategy pre-populated with its configuration.
- **Import**: A builder source mode where the user pastes a `ScenarioConfig` JSON string to pre-populate the builder.
- **NavMenu**: The application-wide navigation sidebar (`NavMenu.razor`) rendered by the Blazor layout.
- **ResearchExplorer**: The page (`ResearchExplorer.razor`) that lists all `StudyRecord` entries with strategy name resolution.
- **StrategyLibrary**: The page (`StrategyLibrary.razor`) that displays all `StrategyIdentity` entries as filterable cards with last-run metrics.
- **ScenarioConfig**: The immutable Core-layer record that is the sole input required to initialise and execute a simulation run.
- **StrategyIdentity**: An Application-layer record representing a user-owned, named research concept that spans multiple versions, runs, and studies.
- **StrategyVersion**: An Application-layer record representing a specific parameterised snapshot of a strategy linked to a `StrategyIdentity`.
- **StudyRecord**: An Application-layer record representing a research workflow execution (sweep, Monte Carlo, walk-forward, etc.) linked to a `StrategyVersion`.
- **BacktestResult**: A Core-layer record containing the full output of a single simulation run including metrics, equity curve, and trade log.
- **SqliteIndexRepository**: An Infrastructure-layer class providing O(log n) SQLite index lookups over the existing JSON file store for `BacktestResult`.
- **IBacktestResultRepository**: The Application-layer interface for querying backtest results, implemented by `SqliteIndexRepository`.
- **StrategyRegistry**: An Application-layer singleton that discovers `IStrategy` implementations via the `[StrategyName]` attribute and resolves them by name.
- **IStrategy**: The Core-layer interface that all strategy implementations must satisfy — consumes `MarketDataEvent` and produces signal/order events.
- **IIndicator**: A proposed Core-layer interface for stateful, composable technical indicators (SMA, EMA, ATR, RSI, etc.).
- **IndicatorRegistry**: A proposed Core-layer registry exposing indicator metadata (name, parameters, output type) for UI discovery.
- **IStrategyIdeaTranslator**: A proposed Application-layer interface for LLM-based translation of natural-language strategy descriptions into structured `StrategyIdeaResult` records.
- **RoslynStrategyCompiler**: A proposed Infrastructure-layer service that compiles user-provided or LLM-generated C# source code into a runtime `IStrategy` implementation using the Roslyn compiler.
- **PromptLoader**: A proposed Infrastructure-layer service that loads LLM system prompt files from disk at startup and exposes them via `IPromptLoader`.
- **RobustnessAdvisoryService**: A proposed Application-layer service that evaluates `BacktestResult` metrics against configurable thresholds and returns warning strings.
- **SignalComposer**: A proposed visual node-based canvas for creating strategy logic by connecting indicator, rule, and action nodes.
- **Stale_Status**: A derived strategy status indicating the most recent run is older than a configurable threshold (default 30 days).
- **CancellationTokenSource**: A .NET type used to propagate cancellation signals to async operations, preventing use-after-dispose exceptions in Blazor components.

---

## Requirements

---

## Phase 1 — Fix Broken Functionality

### Requirement 1: Fix Fork Handler in Step1ChooseStartingPoint

**User Story:** As a strategy researcher, I want to fork an existing strategy in the builder, so that I can create a new strategy pre-populated with a proven configuration as my starting point.

#### Acceptance Criteria

1. WHEN the user selects "Fork Existing" mode and picks a strategy from the dropdown, THE Builder SHALL raise the selected `StrategyIdentity` to the parent `StrategyBuilder` via an `EventCallback<StrategyIdentity>` parameter named `OnForkSelected`.
2. WHEN the parent receives the `OnForkSelected` callback, THE Builder SHALL load the forked strategy's latest `StrategyVersion` and populate `BuilderViewModel` with the version's `StrategyType`, `Parameters`, `Timeframe`, `Hypothesis`, and `DataConfig`.
3. WHEN the fork population completes, THE Builder SHALL set `BuilderViewModel.SourceType` to `SourceType.Fork` and mark the view model as dirty.
4. IF the selected strategy has no saved versions, THEN THE Builder SHALL display a `MudAlert` with severity Warning stating that the strategy has no versions to fork.

### Requirement 2: Fix Import JSON — Parse and Apply

**User Story:** As a strategy researcher, I want to import a strategy configuration by pasting JSON into the builder, so that I can recreate or share configurations without manual re-entry.

#### Acceptance Criteria

1. WHEN the user pastes a JSON string into the Import text field and advances to the next step, THE Builder SHALL deserialise the JSON into a `ScenarioConfig` using `System.Text.Json`.
2. WHEN deserialisation succeeds, THE Builder SHALL raise an `EventCallback<ScenarioConfig>` named `OnImportApplied` to the parent, which populates `BuilderViewModel` with the imported config's `StrategyType`, `Parameters`, `DataProviderOptions`, `SlippageModelType`, `CommissionModelType`, `InitialCash`, and `AnnualRiskFreeRate`.
3. IF deserialisation fails, THEN THE Builder SHALL display a `MudAlert` with severity Error containing the parse error message and remain on Step 1.
4. WHEN import succeeds, THE Builder SHALL advance to Step 2 with all fields pre-populated from the imported config.

### Requirement 3: Fix GoToStep — Allow Navigation to Previously Visited Steps

**User Story:** As a strategy researcher, I want to jump back and forth between previously visited builder steps, so that I can review and adjust earlier decisions without restarting.

#### Acceptance Criteria

1. THE Builder SHALL track a `MaxVisitedStep` property representing the highest step the user has reached during the current session.
2. WHEN the user clicks a step indicator for step N where N is less than or equal to `MaxVisitedStep`, THE Builder SHALL navigate to step N.
3. WHEN the user clicks a step indicator for step N where N is greater than `MaxVisitedStep`, THE Builder SHALL ignore the click and remain on the current step.
4. THE BuilderStepIndicator SHALL visually differentiate three states: "current" (highlighted), "visited" (clickable, distinct style), and "not yet reached" (greyed out, non-interactive).

### Requirement 4: Fix N+1 Queries in ResearchExplorer and StrategyLibrary

**User Story:** As a user browsing strategies or studies, I want pages to load efficiently, so that I do not experience slow page loads caused by sequential per-row repository calls.

#### Acceptance Criteria

1. THE ResearchExplorer SHALL resolve strategy names for all studies using at most two repository calls: one `ListAsync()` for studies and one `ListAsync()` for strategies, joining in memory.
2. THE StrategyLibrary SHALL retrieve version counts for all strategies using a single batch call `GetVersionCountsAsync(IEnumerable<string> strategyIds)` on `IStrategyRepository` instead of one `GetVersionsAsync` call per strategy.
3. WHEN `IStrategyRepository` receives a `GetVersionCountsAsync` call, THE Repository SHALL return an `IReadOnlyDictionary<string, int>` mapping each strategy ID to its version count in a single I/O operation.
4. THE ResearchExplorer page load SHALL complete with O(1) repository calls regardless of the number of studies displayed.

### Requirement 5: Fix IStrategy XML Doc Comment

**User Story:** As a developer reading the codebase, I want the `IStrategy` interface documentation to accurately describe current capabilities, so that I am not misled by outdated scope statements.

#### Acceptance Criteria

1. THE `IStrategy` XML doc comment SHALL accurately describe the V6+ capabilities including `Direction.Long`, `Direction.Short`, and `Direction.Flat` signal semantics.
2. THE `IStrategy` XML doc comment SHALL NOT contain the phrase "all strategies are long-only" or "Short-selling is out of scope".
3. THE `IStrategy` XML doc comment SHALL describe the full position lifecycle: enter long, enter short, exit (flat), and reversal support via `AllowReversals` on `ExecutionConfig`.

### Requirement 6: Delete Orphaned `git` File

**User Story:** As a developer, I want the repository root to be free of accidental artefact files, so that the project structure remains clean and unambiguous.

#### Acceptance Criteria

1. THE zero-byte file named `git` at the repository root SHALL be deleted.
2. THE `.gitignore` file SHALL contain a rule to ignore a file named `git` at the root level to prevent recurrence.

---

## Phase 2 — Navigation & Discovery

### Requirement 7: Restructure NavMenu

**User Story:** As a user, I want the navigation menu to expose all application pages in a logical hierarchy, so that I can discover and reach every feature without memorising URLs.

#### Acceptance Criteria

1. THE NavMenu SHALL contain the following top-level sections in order: DASHBOARD, STRATEGIES, RESEARCH, BACKTESTS, PROP FIRM LAB, DATA, SETTINGS.
2. THE STRATEGIES section SHALL contain links to "My Strategies" (`/strategies/library`) and "New Strategy" (`/strategies/builder`).
3. THE RESEARCH section SHALL contain links to "Explorer" (`/research/explorer`), "Parameter Sweep" (`/research/sweep`), "Monte Carlo" (`/research/montecarlo`), "Walk-Forward" (`/research/walkforward`), "Perturbation" (`/research/perturbation`), "Variance" (`/research/variance`), and "Benchmark" (`/research/benchmark`).
4. THE BACKTESTS link SHALL navigate to `/backtests`.
5. THE DATA section SHALL contain links to "Market Data" (`/market-data`) and "Data Files" (`/data`), and SHALL NOT appear under the SETTINGS section.
6. THE NavMenu SHALL use `MudNavGroup` components with collapsible state persisted to `localStorage` via JS interop so that expanded/collapsed state survives page navigation.

### Requirement 8: Add Launch Actions to Research Explorer

**User Story:** As a strategy researcher, I want to launch new studies directly from the Research Explorer, so that I do not need to memorise direct URLs for each study type.

#### Acceptance Criteria

1. THE ResearchExplorer SHALL display a toolbar above the studies table containing a "New Study" button.
2. WHEN the user clicks "New Study", THE ResearchExplorer SHALL display a menu listing all study types: Sweep, Monte Carlo, Walk-Forward, Perturbation, Variance, and Benchmark.
3. WHEN the user selects a study type from the menu, THE ResearchExplorer SHALL navigate to the corresponding study page, optionally pre-seeding the `StrategyId` query parameter from a selected table row.
4. THE ResearchExplorer SHALL display a "Filter by Strategy" dropdown that sets the existing `?StrategyId=` query parameter to filter the studies table.

### Requirement 9: Add Backtests List Page

**User Story:** As a user, I want to browse all historical backtest runs in a dedicated list page, so that I can find and revisit past results without relying on transient snackbar links.

#### Acceptance Criteria

1. THE System SHALL provide a `BacktestList.razor` page routed at `/backtests` that displays all `BacktestResult` records in a sortable, filterable `MudTable`.
2. THE BacktestList table SHALL display columns for: Strategy Type, Run Date, Sharpe Ratio, Max Drawdown, Total Trades, and Status.
3. THE BacktestList SHALL support sorting by any column and filtering by strategy type and status.
4. THE BacktestList SHALL query results via the SQLite index repository rather than deserialising the full JSON collection.
5. WHEN the user clicks a row, THE BacktestList SHALL navigate to `/backtests/{id}` for the selected result.

### Requirement 10: Implement Stale Strategy Status

**User Story:** As a strategy researcher, I want to see which strategies have not been tested recently, so that I can prioritise re-evaluation of potentially outdated results.

#### Acceptance Criteria

1. THE System SHALL define a `StalenessThresholdDays` configuration value (default: 30) in engine settings, bound via `IOptions<T>`.
2. THE StrategyLibrary SHALL compute `Stale_Status` for each strategy as: the strategy has at least one completed run AND the most recent run date is older than `DateTime.UtcNow` minus `StalenessThresholdDays`.
3. THE StrategyLibrary SHALL enable the "Stale" filter chip (currently disabled with "coming soon") and make it functional for filtering strategies by `Stale_Status`.
4. THE StrategyLibrary SHALL display a "STALE" badge on strategy cards that meet the staleness criteria, using `Color.Warning`.

---

## Phase 3 — Builder & Engine Quality

### Requirement 11: Schema-Driven Validation for Builder Steps 3 and 4

**User Story:** As a strategy researcher, I want the builder to validate my parameter inputs against the strategy's schema before allowing me to proceed, so that I do not launch backtests with invalid configurations.

#### Acceptance Criteria

1. WHEN the user is on Step 3 (Strategy Parameters), THE Builder SHALL validate each parameter value against the active strategy's `StrategyParameterSchema`, checking `[ParameterMeta(Min=..., Max=...)]` bounds.
2. WHEN any parameter value is out of range, THE Builder SHALL display a per-field validation message using MudBlazor form validation and `CanAdvance()` SHALL return `false`.
3. WHEN the user is on Step 4 (Realism & Risk Profile), THE Builder SHALL validate that risk allocation percentages sum to 100% or less and that stop-loss values are positive.
4. WHEN Step 4 validation fails, THE Builder SHALL display per-field validation messages and `CanAdvance()` SHALL return `false`.
5. WHEN all fields on the current step pass validation, THE Builder SHALL allow advancement to the next step.

### Requirement 12: Differentiate Quick Sanity from Standard Backtest

**User Story:** As a strategy researcher, I want a fast "quick sanity" check that runs on reduced data with minimal friction, so that I can get rapid feedback before committing to a full backtest.

#### Acceptance Criteria

1. WHEN the user selects "Quick Sanity" launch action, THE Builder SHALL configure the `ScenarioConfig` with a reduced date range (last 2 years of available data), a single trial (`MaxTrials = 1`), and `ExecutionRealismProfile.NoFriction`.
2. WHEN the user selects "Standard Backtest" launch action, THE Builder SHALL use the full `ScenarioConfig` as configured by the user without modification.
3. WHEN a Quick Sanity run completes, THE result page SHALL display a visible badge or banner indicating "Quick Sanity — reduced dataset" so the user understands the result's limited scope.
4. THE Quick Sanity configuration overrides SHALL NOT persist to the saved `ConfigDraft` or `StrategyVersion` — they are transient, run-time-only modifications.

### Requirement 13: Replace `dynamic` Result Type in HandleRunResult

**User Story:** As a developer, I want the builder's run result handler to use compile-time type safety, so that runtime binding exceptions are eliminated and refactoring is safe.

#### Acceptance Criteria

1. THE `HandleRunResult` method in `StrategyBuilder.razor` SHALL accept a concrete typed parameter (the return type of `RunScenarioUseCase.RunAsync`) instead of `dynamic`.
2. THE method SHALL use pattern matching or a typed `Result<T>` check to determine success or failure.
3. THE System SHALL produce a compile-time error if the result type changes in `RunScenarioUseCase`, rather than a runtime `RuntimeBinderException`.

### Requirement 14: Fix StrategyId Truncation

**User Story:** As a developer, I want strategy IDs to be human-readable, URL-safe, and sufficiently unique, so that ID collisions are practically impossible and IDs are meaningful in logs and URLs.

#### Acceptance Criteria

1. THE Builder SHALL generate strategy IDs using a slug derived from the strategy name (lowercase, hyphens, alphanumeric only) combined with a random suffix of at least 8 hexadecimal characters.
2. THE generated strategy ID SHALL be URL-safe (containing only lowercase letters, digits, and hyphens).
3. THE generated strategy ID SHALL contain at least 32 bits of randomness (8 hex characters) to prevent collisions.
4. IF the strategy name is empty or whitespace, THEN THE Builder SHALL generate a fully random ID with a "strategy-" prefix and at least 8 hex characters of randomness.

### Requirement 15: Add Equity Curve Chart to Backtest Result Page

**User Story:** As a strategy researcher, I want to see a visual equity curve chart on the backtest result page, so that I can quickly assess strategy performance trajectory and drawdown behaviour.

#### Acceptance Criteria

1. THE backtest result page SHALL render an equity curve line chart above the metrics table when the `BacktestResult` contains equity curve data.
2. THE equity curve chart SHALL display the equity value on the primary Y-axis with date on the X-axis.
3. THE equity curve chart SHALL display drawdown as a filled area on a secondary Y-axis or as shading below the equity line.
4. WHEN the `BacktestResult` contains no equity curve data, THE result page SHALL omit the chart section without error.
5. THE chart SHALL use the existing `Plotly.Blazor` package already referenced by the Web project.

### Requirement 16: Extract RobustnessAdvisoryService

**User Story:** As a developer, I want robustness warning logic centralised in a single service, so that threshold changes apply consistently across Dashboard and StrategyLibrary without code duplication.

#### Acceptance Criteria

1. THE System SHALL provide an `IRobustnessAdvisoryService` interface in the Application layer with a method `IReadOnlyList<string> GetWarnings(BacktestResult result)`.
2. THE `RobustnessAdvisoryService` implementation SHALL evaluate configurable thresholds (Sharpe > 3.0, TotalTrades < 30, K-Ratio < 0, MaxDrawdown > 20%) loaded via `IOptions<RobustnessThresholds>`.
3. THE Dashboard SHALL call `IRobustnessAdvisoryService.GetWarnings` instead of inline threshold checks.
4. THE StrategyLibrary SHALL call `IRobustnessAdvisoryService.GetWarnings` instead of inline threshold checks.
5. WHEN a threshold value is changed in configuration, THE change SHALL take effect in both Dashboard and StrategyLibrary without code modification.

---

## Phase 4 — LLM Strategy Assistant

### Requirement 17: IStrategyIdeaTranslator with Provider Abstraction

**User Story:** As a strategy researcher, I want to describe a trading idea in natural language and have an LLM translate it into a structured strategy configuration, so that I can create strategies without deep knowledge of parameter schemas.

#### Acceptance Criteria

1. THE System SHALL provide an `IStrategyIdeaTranslator` interface in the Application layer with a `TranslateAsync` method accepting a user description, available templates, available parameter schemas, and a `CancellationToken`.
2. THE `TranslateAsync` method SHALL return a `StrategyIdeaResult` record containing: `Success`, `SelectedTemplateId`, `StrategyType`, `SuggestedParameters`, `SuggestedHypothesis`, `SuggestedTimeframe`, `FailureReason`, and optional `GeneratedStrategyCode`.
3. THE Infrastructure layer SHALL provide concrete implementations for GoogleAIStudio, Groq, and Ollama LLM providers.
4. THE System SHALL provide a `FallbackStrategyIdeaTranslator` that attempts providers in a configurable chain (default: GoogleAIStudio → Groq → Ollama), advancing to the next provider on failure.
5. WHEN `EnableAIStrategyAssist` is `false` in configuration, THE translator SHALL return `StrategyIdeaResult(Success: false, FailureReason: "AI assist is disabled")` immediately without making any HTTP calls.
6. THE LLM providers SHALL enforce JSON schema in the response format where supported by the provider API.
7. THE System SHALL load the system prompt from a file (`Prompts/strategy-idea-translator-system-prompt.md`) at runtime via `IPromptLoader`, with `{templates_json}` and `{schemas_json}` as interpolation tokens.

### Requirement 18: Add "Describe Your Idea" Step 0 to Strategy Builder

**User Story:** As a strategy researcher, I want a natural-language entry point at the start of the builder, so that I can describe my trading idea in plain English and have the system pre-fill the builder for me.

#### Acceptance Criteria

1. THE Builder SHALL add a Step 0 ("Describe Your Idea") before the existing Step 1, containing a multi-line text field with placeholder "Describe your trading idea in plain language...".
2. THE Step 0 SHALL display a "Use AI to prefill →" button that invokes `IStrategyIdeaTranslator.TranslateAsync` with the user's description.
3. WHEN `EnableAIStrategyAssist` is `false`, THE "Use AI to prefill →" button SHALL be disabled with a tooltip explaining the feature is not configured.
4. THE Step 0 SHALL display a "Skip, start manually →" link that advances to Step 1 without calling the LLM.
5. WHILE the LLM call is in-flight, THE Step 0 SHALL display a `MudProgressLinear` with the text "Analysing your idea…".
6. WHEN the LLM call succeeds, THE Builder SHALL advance to Step 2 with all fields pre-populated from the `StrategyIdeaResult` and display a dismissible `MudAlert` stating "AI pre-filled your strategy — review and adjust below".
7. IF the LLM call fails, THEN THE Step 0 SHALL display the error reason and remain on Step 0 with options to retry or skip.

### Requirement 19: Core Indicators Library

**User Story:** As a developer, I want a shared, composable indicator library in the Core layer, so that strategies can use tested indicator implementations instead of duplicating rolling-window arithmetic inline.

#### Acceptance Criteria

1. THE Core layer SHALL provide an `IIndicator<TOutput>` interface with properties `Value` (nullable until warmed up), `IsReady`, and methods `Update(decimal close, decimal? high, decimal? low)` and `Reset()`.
2. THE Core layer SHALL provide implementations for: `SimpleMovingAverage`, `ExponentialMovingAverage`, `AverageTrueRange`, `RelativeStrengthIndex`, `BollingerBands`, `RollingZScore`, and `DonchianChannel`.
3. WHEN an indicator has not received enough data points to produce a valid output, THE indicator's `Value` SHALL be `null` and `IsReady` SHALL be `false`.
4. WHEN an indicator has received sufficient data points, THE indicator's `Value` SHALL match a known-correct reference value (e.g., Excel-calculated) within a tolerance of 1e-10.
5. THE `ZScoreMeanReversionStrategy` SHALL be refactored to use `RollingZScore` from the indicator library as a proof-of-concept migration.
6. FOR ALL indicator implementations, calling `Reset()` then replaying the same data series SHALL produce identical output values (round-trip determinism property).

### Requirement 20: Dynamic Roslyn Strategy Compiler

**User Story:** As a strategy researcher, I want to register new strategy types at runtime from C# source code, so that LLM-generated or user-written strategies can be tested without recompiling the application.

#### Acceptance Criteria

1. THE System SHALL provide an `IRoslynStrategyCompiler` interface in the Infrastructure layer with a method to compile C# source code into an `IStrategy` implementation at runtime.
2. THE `StrategyCodeSanitiser` SHALL reject source code containing any of: `HttpClient`, `WebClient`, `File.`, `Directory.`, `Process.`, `Environment.`, `Assembly.Load`, `Type.GetType`, `Activator.CreateInstance`, `Marshal`, `unsafe`, `extern`, `DllImport`.
3. THE compiler SHALL load the compiled assembly in a restricted `AssemblyLoadContext` that does not grant file system or network access.
4. IF the compiled type does not implement `IStrategy`, THEN THE compiler SHALL reject the compilation and return a structured error.
5. THE compiler SHALL log the full source code, sanitiser result, and compilation diagnostics to a structured audit log.
6. WHEN `EnableDynamicStrategyCompilation` is `false` in configuration, THE compiler SHALL return an error without attempting compilation.
7. WHEN compilation succeeds, THE `DynamicStrategyRegistry` SHALL register the new strategy type so it is resolvable by `StrategyRegistry.Resolve`.

### Requirement 21: Prompt Management — Load System Prompts from Files

**User Story:** As a developer, I want LLM system prompts loaded from files at runtime, so that prompts can be updated without recompiling the application.

#### Acceptance Criteria

1. THE System SHALL provide an `IPromptLoader` interface with a method `string GetPrompt(string promptName)` that returns the content of a named prompt file.
2. THE `PromptLoader` implementation SHALL read prompt files from the `Prompts/` directory at startup and cache them in memory.
3. WHEN a requested prompt name does not match any loaded file, THE `PromptLoader` SHALL throw a descriptive exception identifying the missing prompt and listing available prompts.
4. THE LLM provider implementations SHALL obtain their system prompts via `IPromptLoader` rather than hardcoded strings.
5. THE `PromptLoader` SHALL support token interpolation, replacing `{templates_json}` and `{schemas_json}` placeholders with provided values at call time.

---

## Phase 5 — Long-Term Architecture

### Requirement 22: SQLite Index for BacktestResult Queries on Web Pages

**User Story:** As a user, I want Dashboard and StrategyLibrary pages to load quickly even with hundreds of backtest results, so that browsing is responsive without deserialising the entire JSON collection.

#### Acceptance Criteria

1. THE Dashboard SHALL retrieve recent runs via a `GetRecentRunsAsync(int limit)` method on `IBacktestResultRepository` that queries the SQLite index and deserialises only the requested number of results.
2. THE Dashboard SHALL retrieve the last run per strategy via a `GetLastRunPerStrategyAsync()` method that returns at most one result per strategy type using a SQLite `GROUP BY` query.
3. THE StrategyLibrary SHALL retrieve run summaries by strategy via a `GetRunSummariesByStrategyAsync(string strategyId)` method that queries the SQLite index.
4. THE `ListAsync()` full-collection deserialisation SHALL NOT be called by Dashboard or StrategyLibrary after this change.

### Requirement 23: Add CancellationToken to Blazor Page Initialisation

**User Story:** As a developer, I want all Blazor pages with async initialisation to honour component disposal, so that use-after-dispose exceptions are prevented when users navigate away during loading.

#### Acceptance Criteria

1. WHEN a Blazor component performs async I/O in `OnInitializedAsync`, THE component SHALL create a `CancellationTokenSource` and pass its `Token` to all async repository and service calls.
2. WHEN the component is disposed, THE component SHALL cancel the `CancellationTokenSource` to signal in-flight operations to stop.
3. THE component SHALL implement `IDisposable` with a `Dispose` method that calls `_cts.Cancel()` followed by `_cts.Dispose()`.
4. THE following pages SHALL be updated: `Dashboard.razor`, `ResearchExplorer.razor`, `StrategyLibrary.razor`, and any other Razor component with async `OnInitializedAsync` and no existing `CancellationToken` handling.

### Requirement 24: Indicator Registry for Visual Composer

**User Story:** As a developer building a future visual strategy composer, I want indicator metadata exposed in a structured registry, so that a drag-and-drop UI can discover available indicators and their parameters.

#### Acceptance Criteria

1. THE Core layer SHALL provide an `IndicatorRegistry` class with a static `All` property returning `IReadOnlyList<IndicatorDescriptor>`.
2. THE `IndicatorDescriptor` record SHALL contain: `Name`, `Description`, `Parameters` (each with `Name`, `Type`, `Min`, `Max`, `Default`), and `OutputType`.
3. THE `IndicatorRegistry.All` SHALL contain one descriptor for each indicator implemented in Requirement 19 (SMA, EMA, ATR, RSI, Bollinger Bands, Z-Score, Donchian Channel).
4. THE `IndicatorDescriptor` format SHALL parallel the existing `DefaultStrategyTemplates.All` pattern used for strategy template discovery.

### Requirement 25: Signal Composer UI (Visual Canvas)

**User Story:** As a non-developer strategy researcher, I want a visual node-based canvas for creating strategy logic, so that I can compose strategies by connecting indicators, rules, and actions without writing code.

#### Acceptance Criteria

1. THE System SHALL provide a `SignalComposer.razor` page routed at `/strategies/composer` containing a node-based visual canvas.
2. THE canvas SHALL support three node types: Indicator nodes (sourced from `IndicatorRegistry.All`), Rule nodes (`CrossAbove`, `CrossBelow`, `GreaterThan`, `LessThan`, `And`, `Or`), and Action nodes (`Enter Long`, `Enter Short`, `Exit`, `Size by ATR`).
3. THE user SHALL be able to drag nodes onto the canvas, connect outputs to inputs via edges, and configure node parameters via inline forms.
4. THE canvas SHALL serialise the node graph to a `ComposerGraph` JSON model that can be persisted and reloaded.
5. THE System SHALL provide a `ComposerToStrategyTranslator` that compiles a `ComposerGraph` into a dynamic `IStrategy` using the Roslyn compiler (Requirement 20) or emits it as a named-template configuration.
6. WHEN the user clicks "Test Strategy", THE composer SHALL compile the graph and launch a backtest using the resulting `IStrategy`.

---

## Dependency Graph

| Item | Depends On |
|------|-----------|
| 1–6 (Phase 1) | Independent — can run in parallel |
| 7–10 (Phase 2) | Independent — can run in parallel |
| 11–16 (Phase 3) | Independent — can run in parallel |
| 17 | None (uses Item 21 for prompt loading when available) |
| 18 | Item 17 |
| 19 | None |
| 20 | Item 19 |
| 21 | None (supports Item 17) |
| 22 | None |
| 23 | None |
| 24 | Item 19 |
| 25 | Items 19, 20, 24 |
