# Implementation Plan: Web-Only UX Overhaul

## Overview

This plan restructures TradingResearchEngine into a Web UI-only application by removing CLI/API projects, overhauling documentation, improving Parameter Sweep and Dashboard UX, extracting oversized Razor page components, and adding property-based and unit tests for new pure functions. Tasks are ordered to minimize broken-build windows: structural removal first, then documentation, then UI features, then refactoring, then tests, then final verification.

## Tasks

- [x] 1. Remove CLI and API projects from solution
  - [x] 1.1 Delete the `src/TradingResearchEngine.Cli/` directory entirely
    - Remove the entire directory tree including all source files, bin/obj, and project file
    - _Requirements: 1.1, 1.2, 2.6_

  - [x] 1.2 Delete the `src/TradingResearchEngine.Api/` directory entirely
    - Remove the entire directory tree including all source files, bin/obj, and project file
    - _Requirements: 2.1, 2.2, 2.5_

  - [x] 1.3 Remove CLI and API project entries from `TradingResearchEngine.sln`
    - Remove Project entry for `TradingResearchEngine.Cli` (GUID `{9954C210-15A3-436D-ABBA-A11D402CC46C}`)
    - Remove Project entry for `TradingResearchEngine.Api` (GUID `{373A1D60-A70B-40A5-8D24-3CD37E837CDB}`)
    - Remove all `ProjectConfigurationPlatforms` entries for those GUIDs
    - Remove all `NestedProjects` entries for those GUIDs
    - _Requirements: 1.1, 2.1_

  - [x] 1.4 Update IntegrationTests project references
    - Remove `ProjectReference` to `TradingResearchEngine.Cli` from `src/TradingResearchEngine.IntegrationTests/TradingResearchEngine.IntegrationTests.csproj`
    - Remove `ProjectReference` to `TradingResearchEngine.Api` from the same file
    - Add `ProjectReference` to `src/TradingResearchEngine.Web/TradingResearchEngine.Web.csproj`
    - _Requirements: 3.2, 2.4, 1.4_

  - [x] 1.5 Remove or update API-specific integration test files
    - Delete `V8EndpointTests.cs` (or equivalent API endpoint test file) from IntegrationTests
    - Update any remaining test files that reference `WebApplicationFactory<Program>` from the API project to use the Web project's `Program` class
    - _Requirements: 3.1, 3.2_

  - [x] 1.6 Add public partial Program class to Web project
    - Add `public partial class Program { }` to `src/TradingResearchEngine.Web/Program.cs` to enable `WebApplicationFactory<Program>` access from integration tests
    - _Requirements: 3.2_

  - [x] 1.7 Verify solution builds after removal
    - Run `dotnet build TradingResearchEngine.sln` and confirm zero errors
    - Verify that `src/TradingResearchEngine.Web/` is the only executable host project remaining under `src/` (no other project with `<OutputType>Exe</OutputType>` or web SDK host exists)
    - _Requirements: 1.2, 1.3, 2.2, 2.3, 3.3, 15.1_

- [x] 2. Checkpoint - Verify CLI/API removal
  - Run `dotnet build TradingResearchEngine.sln` — confirm zero errors
  - Run `dotnet test TradingResearchEngine.sln` — confirm all tests pass (minus deleted API tests)
  - Confirm `src/TradingResearchEngine.Cli/` and `src/TradingResearchEngine.Api/` no longer exist on disk

- [x] 3. Documentation updates
  - [x] 3.1 Create `CHANGELOG.md` at repository root
    - Extract all version-history and product-goals notes from the current README into `CHANGELOG.md`
    - Organize chronologically with version identifiers (V1, V2, V2.1, V3, V6, etc.)
    - _Requirements: 5.1, 5.2, 5.3_

  - [x] 3.2 Overhaul `README.md` for Web-only presentation
    - Rewrite as a Web UI product page with "Getting Started" section within the first 40 lines
    - Include commands: `dotnet build`, `dotnet test`, `dotnet run --project src/TradingResearchEngine.Web`
    - State the URL/port the application opens on after launch
    - State that the Dashboard is the landing page and describe what the user sees on first launch
    - Remove all CLI/API usage instructions and references to `TradingResearchEngine.Cli` or `TradingResearchEngine.Api` as supported entry points
    - Add a "Documentation" section linking each file in `docs/` with a one-line description
    - Add a link to `CHANGELOG.md`
    - Update architecture diagram to list only: Core, Application, Infrastructure, Web, Benchmarks, UnitTests, IntegrationTests
    - State dependency rule as `Core ← Application ← Infrastructure ← Web`
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7, 4.8, 4.9, 14.2, 14.3_

  - [x] 3.3 Update `docs/UI-Planning-Specification.md` to remove CLI references
    - Remove references to CLI as a parallel host option
    - Update any architecture descriptions to reflect Web-only posture
    - _Requirements: 17.3_

  - [x] 3.4 Scan and update remaining docs for CLI/API references
    - Search all files in `docs/` for references to `TradingResearchEngine.Cli` or `TradingResearchEngine.Api`
    - Update or remove any references to CLI or API as active/supported host options
    - _Requirements: 14.1, 17.1, 17.2_

- [x] 4. Checkpoint - Verify documentation updates
  - Run `dotnet build TradingResearchEngine.sln` — confirm build still passes after doc changes
  - Search repository for "TradingResearchEngine.Cli" and "TradingResearchEngine.Api" in README.md and docs/ — confirm zero results
  - Verify `CHANGELOG.md` exists at repository root and contains version history sections
  - Verify README.md "Getting Started" section appears within the first 40 lines

- [x] 5. Parameter Sweep UX improvements
  - [x] 5.1 Create `SweepRowModel.cs` in `src/TradingResearchEngine.Web/Features/Research/Sweep/`
    - Implement the `SweepRowModel` class with `ParameterName`, `Low`, `High`, and `Increment` properties as defined in the design
    - _Requirements: 7.1_

  - [x] 5.2 Create `SweepRangeGenerator.cs` in `src/TradingResearchEngine.Web/Features/Research/Sweep/`
    - Implement the static `Generate(decimal low, decimal high, decimal increment)` method
    - Return `null` for invalid inputs (Increment ≤ 0 or Low > High)
    - Generate inclusive range list [Low, Low+Increment, ..., High] for valid inputs
    - _Requirements: 7.2, 7.3, 7.4_

  - [x] 5.3 Create `SweepParameterSelector.cs` in `src/TradingResearchEngine.Web/Features/Research/Sweep/`
    - Implement the static `SelectNext(IReadOnlyList<StrategyParameterSchema> schema, IReadOnlySet<string> usedNames)` method
    - Return the first schema parameter name not in `usedNames`, or null if all are used
    - _Requirements: 8.1, 8.2_

  - [x] 5.4 Create `SweepParameterRow.razor` in `Components/Pages/Research/`
    - Implement the self-contained row component with parameters: `Schema`, `UsedParametersByOtherRows`, `OnChanged`, `OnRemoved`, `Model`
    - When Schema is non-null/non-empty: render `MudSelect<string>` dropdown populated from schema parameter names
    - When Schema is null/empty: render `MudTextField<string>` for free-text parameter name (fallback)
    - Display helper text (Description + SensitivityHint) when a parameter is selected from dropdown
    - Exclude parameters used by other rows from dropdown options (or show as disabled)
    - Always include the row's own currently-selected parameter as a visible option
    - Render three numeric fields: Low, High, Increment
    - Display inline validation errors when `SweepRangeGenerator.Generate` returns null
    - _Requirements: 6.2, 6.3, 6.4, 7.1, 7.3, 7.4, 8.1_

  - [x] 5.5 Update `Sweep.razor` to use schema-driven selection and range inputs
    - Load `Strategy_Schema` via `IStrategySchemaProvider` when a strategy version is selected
    - Replace inline `GridEntry` rendering with `SweepParameterRow` components
    - Compute `UsedParametersByOtherRows` for each row and re-render siblings on change
    - Auto-select first unused parameter for new rows using `SweepParameterSelector.SelectNext`
    - Validate all rows via `SweepRangeGenerator` before executing sweep
    - Prevent sweep execution when any row has invalid range inputs
    - Handle schema load failure gracefully (catch exception, fall back to free-text)
    - _Requirements: 6.1, 6.2, 6.3, 6.5, 7.2, 8.1, 8.2_

- [x] 6. Dashboard KPI usability improvements
  - [x] 6.1 Add strategy name resolution to Last Sharpe tile on `Dashboard.razor`
    - Implement `GetLastSharpeStrategyName()` method that resolves strategy name from `_strategies` collection or falls back to `StrategyType` string
    - Display strategy name caption below the Sharpe value
    - Hide strategy caption when no completed runs exist
    - Display "—" as the value when no completed runs exist
    - _Requirements: 9.1, 9.2, 9.3_

  - [x] 6.2 Add click navigation to Last Sharpe tile
    - Implement `GetLastSharpeNavigationTarget()` method
    - If strategy ID resolvable → navigate to `/strategies/{strategyId}`
    - If no strategy ID but run exists → navigate to `/backtests/history`
    - If no runs → tile is not clickable (no cursor:pointer, no onclick)
    - Add conditional `cursor:pointer` styling only when clickable
    - _Requirements: 10.1, 10.2, 10.3_

  - [x] 6.3 Fix robustness warnings to use most recent completed runs
    - Update robustness warnings panel to filter `_runs` to `Status == Completed` and take first 10 from the existing descending RunId ordering
    - Add explicit code comment documenting that `_runs` ordering is the canonical recency source and why RunId descending satisfies "most recent by run date"
    - Do NOT create a second independent recency sort
    - _Requirements: 11.1, 11.2, 11.3_

- [x] 7. Checkpoint - Verify UI changes
  - Run `dotnet build TradingResearchEngine.sln` — confirm zero errors after Sweep and Dashboard changes
  - Run any existing unit tests related to sweep or dashboard logic to confirm no regressions
  - Visually confirm the new `Features/Research/Sweep/` files compile without errors

- [x] 8. Extract ResultDetail sub-components
  - [x] 8.1 Create `ResultMetricsPanel.razor` in `Components/Pages/Backtests/`
    - Extract Tier 1 metrics grid (Sharpe, Max DD, Win Rate, K-Ratio, DSR, Trial #) and Tier 2 tabbed metrics (Extended Metrics, Trade Stats) from `ResultDetail.razor`
    - Accept `[Parameter, EditorRequired] public BacktestResult Result { get; set; }` 
    - _Requirements: 12.1_

  - [x] 8.2 Create `ResultEquityCurvePanel.razor` in `Components/Pages/Backtests/`
    - Extract equity curve chart and Charts tab content (equity+drawdown, monthly returns, trade PnL, holding period) from `ResultDetail.razor`
    - Accept `[Parameter, EditorRequired] public BacktestResult Result` and `[Parameter] public IReadOnlyList<BarRecord>? BarRecords`
    - _Requirements: 12.2_

  - [x] 8.3 Create `ResultTradeLogPanel.razor` in `Components/Pages/Backtests/`
    - Extract trades table with pagination from `ResultDetail.razor`
    - Accept `[Parameter, EditorRequired] public IReadOnlyList<ClosedTrade> Trades`
    - _Requirements: 12.3_

  - [x] 8.4 Create `ResultRealismPanel.razor` in `Components/Pages/Backtests/`
    - Extract realism assumptions card, robustness warnings, and IS vs OOS panel from `ResultDetail.razor`
    - Accept `[Parameter, EditorRequired] public BacktestResult Result`
    - _Requirements: 12.4_

  - [x] 8.5 Refactor `ResultDetail.razor` to use extracted sub-components
    - Retain `@page "/backtests/{Id}"` route, all `@inject` directives, `OnInitializedAsync` data loading, header, breadcrumbs, status banner, action band, export menu, and tab container structure
    - Replace inline markup with `<ResultMetricsPanel>`, `<ResultEquityCurvePanel>`, `<ResultTradeLogPanel>`, `<ResultRealismPanel>` component references
    - Verify identical visible output after refactoring
    - Verify existing route behavior is preserved (`/backtests/{Id}` resolves identically)
    - Verify all navigation links within the page (breadcrumbs, action buttons) continue to target the same URLs
    - Verify all action handlers (export, study launch, prop firm evaluation, AI refine) continue to function identically
    - Verify tab switching behavior remains unchanged
    - _Requirements: 12.5, 12.6_

- [x] 9. Extract StrategyDetail sub-components
  - [x] 9.1 Create `StrategyOverviewPanel.razor` in `Components/Pages/Strategies/`
    - Extract latest run summary, benchmark chip, robustness warnings, equity curve, development stage, research progress, recommended next study, and quick actions from `StrategyDetail.razor`
    - Accept parameters as defined in design: `Strategy`, `LatestRun`, `Checklist`, `Descriptor`, `BenchmarkExcessSharpe`, `OnRunRequested`, `SelectedVersion`
    - _Requirements: 13.1_

  - [x] 9.2 Create `StrategyVersionsPanel.razor` in `Components/Pages/Strategies/`
    - Extract version parameters table, execution config table, and execution window display + edit button from `StrategyDetail.razor`
    - Accept parameters: `Version`, `Strategy`, `ExecWindow`, `OnEditWindowRequested`
    - _Requirements: 13.2_

  - [x] 9.3 Create `StrategyRunsPanel.razor` in `Components/Pages/Strategies/`
    - Extract KPI summary bar and runs table from `StrategyDetail.razor`
    - Accept parameters: `Runs`, `LatestRun`
    - _Requirements: 13.3_

  - [x] 9.4 Create `StrategyStudiesPanel.razor` in `Components/Pages/Strategies/`
    - Extract study launch bar and studies table from `StrategyDetail.razor`
    - Accept parameters: `Strategy`, `SelectedVersion`, `LatestRun`, `Studies`, `Descriptor`
    - _Requirements: 13.4_

  - [x] 9.5 Refactor `StrategyDetail.razor` to use extracted sub-components
    - Retain `@page "/strategies/{StrategyId}"` route, all `@inject` directives, `OnInitializedAsync` and `LoadVersionData` logic, header band, tab container (`MudTabs`), all dialogs, Prop Firm tab (inline), and `@code` block with state fields and event handlers
    - Replace inline tab content with `<StrategyOverviewPanel>`, `<StrategyVersionsPanel>`, `<StrategyRunsPanel>`, `<StrategyStudiesPanel>` component references
    - Verify identical visible output and preserved tab switching, navigation links, action handlers, and dialog behavior
    - _Requirements: 13.5, 13.6, 13.7_

- [x] 10. Checkpoint - Verify component extraction
  - Run `dotnet build TradingResearchEngine.sln` — confirm zero errors after component extraction
  - Run `dotnet test TradingResearchEngine.sln` — confirm all tests pass
  - Verify no new `@page` routes were introduced by extracted sub-components

- [x] 11. Property-based tests (FsCheck.Xunit)
  - [x] 11.1 Write property test for range generation correctness
    - **Property 1: Range generation produces correct sequence**
    - For any valid inputs (Low ≤ High, Increment > 0), verify: first element equals Low, each subsequent element equals previous + Increment, all elements ≤ High, list length equals `floor((high - low) / increment) + 1`
    - Use `[Property(MaxTest = 100)]` attribute
    - Tag with `// Feature: web-only-ux-overhaul, Property 1: Range generation produces correct sequence`
    - Place in `src/TradingResearchEngine.UnitTests/`
    - **Validates: Requirements 7.2**

  - [x] 11.2 Write property test for range generation invalid input rejection
    - **Property 2: Range generation rejects invalid inputs**
    - For any inputs where Increment ≤ 0 OR Low > High, verify `SweepRangeGenerator.Generate` returns null
    - Use `[Property(MaxTest = 100)]` attribute
    - Tag with `// Feature: web-only-ux-overhaul, Property 2: Range generation rejects invalid inputs`
    - Place in `src/TradingResearchEngine.UnitTests/`
    - **Validates: Requirements 7.3, 7.4**

  - [x] 11.3 Write property test for auto-selection of unused parameters
    - **Property 3: Auto-selection picks first unused parameter**
    - For any non-empty schema list and any subset of used names, verify `SweepParameterSelector.SelectNext` returns the first schema entry not in usedNames, or null if all used
    - Use `[Property(MaxTest = 100)]` attribute
    - Tag with `// Feature: web-only-ux-overhaul, Property 3: Auto-selection picks first unused parameter`
    - Place in `src/TradingResearchEngine.UnitTests/`
    - **Validates: Requirements 8.1, 8.2**

  - [x] 11.4 Write property test for robustness warnings filtering
    - **Property 4: Robustness warnings evaluate only completed runs in recency order**
    - For any list of BacktestResult items with mixed statuses, verify filtering to Completed + take 10 by descending RunId produces: all items Completed, count ≤ 10, ordered by RunId descending, no non-Completed runs
    - Use `[Property(MaxTest = 100)]` attribute
    - Tag with `// Feature: web-only-ux-overhaul, Property 4: Robustness warnings evaluate only completed runs in recency order`
    - Place in `src/TradingResearchEngine.UnitTests/`
    - **Validates: Requirements 11.1, 11.3**

- [x] 12. Example-based unit tests (xUnit)
  - [x] 12.1 Write unit tests for `SweepRangeGenerator`
    - `SweepRangeGenerator_ZeroIncrement_ReturnsNull` (Req 7.3)
    - `SweepRangeGenerator_NegativeIncrement_ReturnsNull` (Req 7.3)
    - `SweepRangeGenerator_LowGreaterThanHigh_ReturnsNull` (Req 7.4)
    - `SweepRangeGenerator_LowEqualsHigh_ReturnsSingleElement` (Req 7.2 edge case)
    - Place in `src/TradingResearchEngine.UnitTests/`
    - _Requirements: 7.2, 7.3, 7.4_

  - [x] 12.2 Write unit tests for `SweepParameterSelector`
    - `SweepParameterSelector_AllUsed_ReturnsNull` (Req 8.2)
    - `SweepParameterSelector_EmptySchema_ReturnsNull` (Req 8.2 edge case)
    - Place in `src/TradingResearchEngine.UnitTests/`
    - _Requirements: 8.1, 8.2_

  - [x] 12.3 Write unit test for Dashboard Last Sharpe tile navigation fallback
    - `LastSharpeTile_NoStrategyId_NavigatesToBacktestsHistory` — verify that when no strategy ID can be resolved from the latest run, the navigation target is `/backtests/history`
    - Place in `src/TradingResearchEngine.UnitTests/`
    - _Requirements: 10.2_

  - [x] 12.4 Write unit test for SweepParameterRow duplicate-parameter prevention
    - `SweepParameterRow_PreservesOwnSelection_AndPreventsDuplicateSelectionFromOtherRows` — verify that a row's own selected parameter remains visible/selectable while parameters used by other rows are excluded or disabled
    - Place in `src/TradingResearchEngine.UnitTests/`
    - _Requirements: 8.1_

- [x] 13. Final build verification and cleanup
  - [x] 13.1 Run full solution build and test suite
    - Execute `dotnet build TradingResearchEngine.sln` — zero errors
    - Execute `dotnet test TradingResearchEngine.sln` — all tests pass
    - Verify Web_Host is the only runnable host project
    - _Requirements: 15.1, 15.2, 15.3_

  - [x] 13.2 Verify no lingering CLI/API references
    - Search repository for strings "TradingResearchEngine.Cli" and "TradingResearchEngine.Api" in all `.csproj`, `.sln`, docs, and README files
    - Confirm zero results in user-facing documentation
    - Remove or update any remaining code comments referencing CLI/API usage instructions
    - _Requirements: 17.1, 17.2_

  - [x] 13.3 Verify Core and Application projects are unmodified
    - Confirm zero source file modifications in `src/TradingResearchEngine.Core/`
    - Confirm zero modifications to engine logic, research workflow logic, or domain calculations in `src/TradingResearchEngine.Application/`
    - _Requirements: 16.1, 16.2, 16.3_

- [x] 14. Final checkpoint - Confirm all acceptance criteria met
  - Run `dotnet build TradingResearchEngine.sln` — zero errors
  - Run `dotnet test TradingResearchEngine.sln` — all tests pass
  - Confirm repository search for "TradingResearchEngine.Cli" and "TradingResearchEngine.Api" returns zero results in `.csproj`, `.sln`, README, and docs
  - Confirm `src/TradingResearchEngine.Web/` is the only executable host project under `src/`
  - Confirm Core and Application projects have zero source modifications to engine/workflow/domain logic

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation after each major phase
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- The `samples/` directory is retained as test data infrastructure (referenced by integration tests)
- Core and Application projects must have zero source modifications (Requirement 16)
- All new pure functions (`SweepRangeGenerator`, `SweepParameterSelector`) are placed in `Features/Research/Sweep/` per design decision
- `SweepParameterRow.razor` stays co-located with `Sweep.razor` in `Components/Pages/Research/`
