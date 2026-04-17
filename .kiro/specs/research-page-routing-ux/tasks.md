# Implementation Plan: Research Page Routing & UX (V7)

## Overview

Seven sequential fixes that close the gap between the fully-working Monte Carlo research page and the five remaining research pages. Each fix unblocks the next — work in order. All code is C# / Blazor Server (.NET 8). Run `dotnet test` after each fix.

## Tasks

- [x] 1. Fix 1 — Add `GetVersionAsync` to `IStrategyRepository`
  - [x] 1.1 Add `GetVersionAsync(string strategyVersionId, CancellationToken ct)` to `IStrategyRepository.cs` interface in `Application/Strategy/`
    - Returns `Task<StrategyVersion?>`, null if not found
    - _Requirements: 14.1_
  - [x] 1.2 Implement `GetVersionAsync` in `JsonStrategyRepository.cs` in `Infrastructure/Persistence/`
    - Two-level directory scan: iterate `strategies/{strategyId}/versions/`, check for `{strategyVersionId}.json` file existence before deserializing
    - Return null if base directory does not exist (no exception)
    - _Requirements: 14.2, 14.3, 14.4, 14.5_
  - [x] 1.3 Replace private `GetVersionAsync` in `ResearchChecklistService.cs` with a call to `_strategyRepo.GetVersionAsync(versionId, ct)`
    - Remove the O(n×m) `ListAsync` → `foreach` → `GetVersionsAsync` → `FirstOrDefault` loop (lines ~195–203)
    - _Requirements: 14.6_
  - [x] 1.4 Replace O(n×m) nested loop in `StudyDetail.razor` `OnInitializedAsync` with `GetVersionAsync` + `GetAsync`
    - `var version = await StrategyRepo.GetVersionAsync(_study.StrategyVersionId);` then `var strategy = await StrategyRepo.GetAsync(version.StrategyId);`
    - _Requirements: 14.7_
  - [x] 1.5 Replace O(n×m) nested loop in `ResearchExplorer.razor` `OnInitializedAsync` with `GetVersionAsync` for each study
    - _Requirements: 14.8_
  - [ ]* 1.6 Write unit tests in `UnitTests/V7/GetVersionAsyncTests.cs`
    - Test: returns correct version when file exists
    - Test: returns null when `strategyVersionId` does not match any file
    - Test: handles empty base directory gracefully (returns null, no exception)
    - _Requirements: 14.2, 14.3, 14.4, 14.5_
  - [ ]* 1.7 Write property test for GetVersionAsync round-trip in `UnitTests/V7/GetVersionAsyncProperties.cs`
    - **Property 5: GetVersionAsync round-trip**
    - **Validates: Requirements 14.2, 14.3**

- [x] 2. Checkpoint — Ensure all tests pass after Fix 1
  - Ensure all tests pass, ask the user if questions arise.

- [x] 3. Fix 2 — Add `StudyType` enum values + BackgroundStudyService dispatch
  - [x] 3.1 Add `BenchmarkComparison`, `Variance`, and `RandomisedOos` values to `StudyType` enum in `StudyRecord.cs`
    - Each must have a distinct integer value (no duplicates, excluding existing `Cpcv` alias)
    - Add XML doc comments for each new value
    - _Requirements: 6.1, 6.2, 6.3, 6.5_
  - [x] 3.2 Wire dispatch for new study types in the Web host dispatcher
    - Find the actual dispatch location (likely `StudyExecutionService` or similar in the Web project) that maps `StudyType` to workflow classes
    - Add cases: `BenchmarkComparison` → `BenchmarkComparisonWorkflow`, `Variance` → `VarianceTestingWorkflow`, `RandomisedOos` → `RandomizedOosWorkflow`
    - Follow existing pattern: inject workflow, call `RunAsync`, serialize result, call `SaveResultAsync`, mark study completed
    - _Requirements: 15.1, 15.2, 15.3, 15.4_
  - [ ]* 3.3 Write unit tests in `UnitTests/V7/StudyTypeEnumTests.cs`
    - Test: `StudyType.BenchmarkComparison` has a distinct integer value
    - Test: `StudyType.Variance` has a distinct integer value
    - Test: `StudyType.RandomisedOos` has a distinct integer value
    - Test: no two enum values share the same integer (excluding `Cpcv` alias)
    - _Requirements: 6.1, 6.2, 6.3, 6.5_
  - [ ]* 3.4 Write property test for StudyType JSON round-trip in `UnitTests/V7/StudyTypeJsonRoundTripProperties.cs`
    - **Property 1: StudyType JSON serialization round-trip**
    - **Validates: Requirements 6.4**


- [x] 4. Fix 3 — `ResultJson` persistence + `StudyDetail` rendering + `BenchmarkExcessSharpe` chip
  - [x] 4.1 Add `string? ResultJson = null` parameter to `StudyRecord` in `StudyRecord.cs`
    - Add as the last optional parameter in the record constructor
    - Add XML doc comment: `V7: Serialized JSON of the workflow result. Null until the study completes.`
    - _Requirements: 10.2_
  - [x] 4.2 Add `SaveResultAsync(string studyId, string resultJson, CancellationToken ct)` to `IStudyRepository.cs`
    - _Requirements: 10.1_
  - [x] 4.3 Implement `SaveResultAsync` in `JsonStudyRepository.cs`
    - Load study via `GetAsync`, return if null, save with `study with { ResultJson = resultJson }`
    - _Requirements: 10.1_
  - [x] 4.4 Serialize and persist results in the workflow orchestrator after each workflow's `RunAsync` completes
    - Use a shared static `JsonSerializerOptions` instance (WriteIndented = false)
    - Apply to all 11 workflow types: MonteCarlo, WalkForward, AnchoredWalkForward, ParameterSweep, Sensitivity, Realism, RegimeSegmentation, BenchmarkComparison, Variance, RandomisedOos, Cpcv
    - Call `SaveResultAsync` before marking study `Completed`
    - _Requirements: 10.3, 10.4_
  - [x] 4.5 Render results in `StudyDetail.razor` when `ResultJson` is non-null and status is `Completed`
    - Deserialize based on `_study.Type` to the appropriate result type
    - MonteCarlo → `MonteCarloFanChart` + `EquityDistributionChart`
    - WalkForward/AnchoredWalkForward → `WalkForwardCompositeChart`
    - ParameterSweep/Sensitivity → `ParameterSweepHeatmap`
    - Realism → metric cards (MeanSharpe, StdDevSharpe, MeanExpectancy, WorstSharpe, BestSharpe)
    - BenchmarkComparison → metric cards (StrategyReturn, BenchmarkReturn, Alpha, Beta, InformationRatio, TrackingError)
    - Cpcv → metric cards (MedianOosSharpe, ProbabilityOfOverfitting, PerformanceDegradation)
    - Variance → variant table (PresetName, Sharpe, MaxDD, WinRate, Trades, EndEquity)
    - Catch `JsonException` on deserialization failure, show error alert, render metadata only
    - When `ResultJson` is null, display only existing metadata and interpretation sections
    - _Requirements: 11.1, 11.2, 11.3, 11.4, 11.5, 11.6, 11.7, 11.8, 11.9_
  - [x] 4.6 Wire `BenchmarkExcessSharpe` chip in `StrategyDetail.razor` `LoadVersionData()`
    - Replace the hardcoded `_benchmarkExcessSharpe = _latestRun.SharpeRatio` approximation
    - Query `StudyRepo.ListByVersionAsync(_selectedVersionId)`, filter to `StudyType.BenchmarkComparison` + `Completed` + non-null `ResultJson`, order by `CreatedAt` descending, take first
    - Deserialize `BenchmarkComparisonResult` and read `ExcessReturn` (NOT `ExcessSharpe`)
    - When no completed benchmark study exists, set `_benchmarkExcessSharpe = null` (tooltip already shows guidance)
    - Do NOT use `_latestRun.SharpeRatio` as fallback
    - _Requirements: 16.1, 16.2, 16.3, 16.4_
  - [ ]* 4.7 Write unit tests in `UnitTests/V7/StudyResultPersistenceTests.cs`
    - Test: `SaveResultAsync` updates the `ResultJson` field and persists
    - Test: round-trip save then load → `ResultJson` equals original serialized string
    - Test: `ResultJson` is null for a study in `Running` state
    - _Requirements: 10.1, 10.2_
  - [ ]* 4.8 Write property test for StudyDetail result deserialization round-trip in `UnitTests/V7/StudyResultRoundTripProperties.cs`
    - **Property 3: StudyDetail result deserialization round-trip**
    - **Validates: Requirements 11.1**

- [x] 5. Checkpoint — Ensure all tests pass after Fixes 2–3
  - Ensure all tests pass, ask the user if questions arise.

- [x] 6. Fix 4 — `StrategyVersionPicker` + query parameter wiring + launch buttons
  - [x] 6.1 Create `StrategyVersionPicker.razor` in `Web/Components/Builder/`
    - Parameters: `PreselectedStrategyId: string?`, `PreselectedVersionId: string?`, `OnSelectionChanged: EventCallback<(StrategyIdentity Strategy, StrategyVersion Version, BacktestResult? LatestRun)>`
    - Load all non-retired strategies from `IStrategyRepository` on init
    - Strategy dropdown → version dropdown → emit selection with latest `BacktestResult`
    - Auto-select strategy if `PreselectedStrategyId` provided, load versions
    - Auto-select version if `PreselectedVersionId` provided, emit event
    - Auto-select single version when only one exists
    - Filter out retired strategies (`Stage == DevelopmentStage.Retired`)
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5, 8.6, 8.7, 8.8_
  - [x] 6.2 Wire query parameters in `WalkForward.razor`
    - Add `[SupplyParameterFromQuery] public string? StrategyId` and `VersionId`
    - Add `StrategyVersionPicker` above window configuration
    - On version selected: call `_configEditor!.LoadFromConfig(version.BaseScenarioConfig)`
    - Wrap `ScenarioConfigEditor` in a collapsed `MudExpansionPanel` ("Advanced / Manual Config")
    - Handle invalid `versionId` with `ISnackbar` warning
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 9.4, 9.6, 9.7_
  - [x] 6.3 Wire query parameters in `Sweep.razor`
    - Same pattern as WalkForward: `StrategyId`, `VersionId` query params, picker above parameter grid
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 9.5, 9.6, 9.7_
  - [x] 6.4 Wire query parameters in `Perturbation.razor`
    - Add `StrategyId`, `VersionId`, and `ResultId` query params
    - Add `StrategyVersionPicker` as primary input above workflow options
    - If `ResultId` provided, load `BacktestResult` and call `LoadFromConfig` with result's config
    - _Requirements: 3.1, 3.2, 3.3, 9.1, 9.6, 9.7_
  - [x] 6.5 Wire query parameters in `Benchmark.razor`
    - Add `StrategyId`, `VersionId`, and `ResultId` query params
    - Add `StrategyVersionPicker` as primary input
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 9.2, 9.6, 9.7_
  - [x] 6.6 Wire query parameters in `Variance.razor`
    - Add `StrategyId`, `VersionId`, and `ResultId` query params
    - Add `StrategyVersionPicker` as primary input
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 9.3, 9.6, 9.7_
  - [x] 6.7 Add Benchmark and Variance launch buttons to `StrategyDetail.razor` Research tab launch bar
    - Add "Benchmark" button: `Href="/research/benchmark?strategyId={strategyId}&versionId={selectedVersionId}"`
    - Add "Variance" button: `Href="/research/variance?strategyId={strategyId}&versionId={selectedVersionId}"`
    - Update `GetStudyLaunchUrl()` to map `StudyType.BenchmarkComparison` → `/research/benchmark?strategyId=...&versionId=...` and `StudyType.Variance` → `/research/variance?strategyId=...&versionId=...`
    - _Requirements: 7.1, 7.2, 7.3, 7.4_
  - [ ]* 6.8 Write property test for StrategyVersionPicker retired filtering in `UnitTests/V7/StrategyVersionPickerProperties.cs`
    - **Property 2: StrategyVersionPicker excludes retired strategies**
    - **Validates: Requirements 8.8**
  - [ ]* 6.9 Write unit test for `GetStudyLaunchUrl` in `UnitTests/V7/GetStudyLaunchUrlTests.cs`
    - Test: `BenchmarkComparison` → URL contains `/research/benchmark?strategyId=...&versionId=...`
    - Test: `Variance` → URL contains `/research/variance?strategyId=...&versionId=...`
    - _Requirements: 7.3, 7.4_

- [x] 7. Checkpoint — Ensure all tests pass after Fix 4
  - Ensure all tests pass, ask the user if questions arise.

- [x] 8. Fix 5 — Timeframe options in Edit Execution Window dialog
  - [x] 8.1 Replace hardcoded `MudSelectItem` list in `StrategyDetail.razor` Edit Execution Window dialog with `TimeframeOptions.All` loop
    - Use `@foreach (var tf in TimeframeOptions.All)` pattern matching `Step2DataExecutionWindow.razor`
    - Display `@tf.Label (@tf.BarsPerYear.ToString("N0") bars/year)` with `Value="@tf.Value"`
    - _Requirements: 13.1, 13.2, 13.3_
  - [x] 8.2 Auto-populate `BarsPerYear` on timeframe selection change in the Edit Execution Window dialog
    - Look up `BarsPerYear` from the selected `TimeframeOption` in `TimeframeOptions.All`
    - `Step2DataExecutionWindow.razor` already handles this — no changes needed there
    - _Requirements: 13.4, 13.5_
  - [ ]* 8.3 Write unit tests in `UnitTests/V7/TimeframeOptionsTests.cs`
    - Test: `TimeframeOptions.All` contains exactly 8 entries
    - Test: all 8 values map to a non-zero `BarsPerYearDefaults` constant
    - _Requirements: 13.3_
  - [ ]* 8.4 Write property test for TimeframeOption BarsPerYear consistency in `UnitTests/V7/TimeframeOptionBarsPerYearProperties.cs`
    - **Property 4: TimeframeOption BarsPerYear consistency**
    - **Validates: Requirements 13.4**

- [x] 9. Fix 6 — Strategy Library retired counter
  - [x] 9.1 Add "N retired hidden" counter to `StrategyLibrary.razor` toolbar
    - Compute `var retiredCount = _strategies.Count(s => s.Stage == DevelopmentStage.Retired);`
    - Display `<MudText Typo="Typo.caption" Class="text-muted">@retiredCount retired hidden</MudText>` when `!_showRetired && retiredCount > 0`
    - Place between the `MudSwitch` and the "New Strategy" button
    - Everything else (toggle, filtering, retirement dialog, opacity, RETIRED chip) already exists
    - _Requirements: 17.1, 17.2, 17.3, 17.4, 17.5, 17.6, 17.7_

- [x] 10. Fix 7 — ResearchExplorer row click navigation
  - [x] 10.1 Add row click navigation to `ResearchExplorer.razor` MudTable
    - Add `OnRowClick="@(args => Nav.NavigateTo($"/research/study/{args.Item.Study.StudyId}"))"` to the `MudTable`
    - Add `Style="cursor:pointer"` to the table
    - `Hover="true"` is already present
    - _Requirements: 12.1_
  - [x] 10.2 Add action column with `MudIconButton` to each row
    - Add a column with `MudIconButton` (open icon) pointing to `/research/study/{context.Study.StudyId}`
    - _Requirements: 12.2_

- [x] 11. Final checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation after each fix
- Property tests validate the 5 universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- The implementation order matches the design's dependency chain: Fix 1 → Fix 2 → Fix 3 → Fix 4 → Fix 5/6/7
- UnitTests reference Core and Application only — never Infrastructure or Web (per testing-standards.md)
- `GetVersionAsync` implementation tests that need real file I/O belong in IntegrationTests, but the mock-based contract tests go in UnitTests
