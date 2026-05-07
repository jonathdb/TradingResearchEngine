# Requirements Document

## Introduction

This feature restructures TradingResearchEngine into a Web UI-only application by removing the CLI and API host projects, overhauling the README for first-run usability, improving the Parameter Sweep and Dashboard UX, and refactoring oversized Razor pages into smaller components. The backtesting engine logic remains unchanged.

## Glossary

- **Web_Host**: The Blazor Server application at `src/TradingResearchEngine.Web/` serving as the sole entry point for the product.
- **CLI_Project**: The `TradingResearchEngine.Cli` project and all associated artifacts (source, references, samples, documentation).
- **API_Project**: The `TradingResearchEngine.Api` project and all associated artifacts (source, references, documentation).
- **Solution_File**: The `TradingResearchEngine.sln` file at the repository root that defines which projects are included in the build.
- **Parameter_Sweep_Page**: The Razor page at `Components/Pages/Research/Sweep.razor` that configures and runs parameter grid searches.
- **Strategy_Schema**: The typed parameter metadata provided by `IStrategySchemaProvider` for a given strategy type, including parameter names, types, defaults, descriptions, and sensitivity hints.
- **Dashboard**: The root landing page (`/`) showing KPI tiles, recent runs, robustness warnings, and navigation cards.
- **Last_Sharpe_Tile**: The KPI tile on the Dashboard displaying the Sharpe ratio from the most recent completed backtest run.
- **Robustness_Warnings_Panel**: The sidebar panel on the Dashboard that displays robustness flags for recent completed runs.
- **ResultDetail_Page**: The Razor page at `Components/Pages/Backtests/ResultDetail.razor` displaying detailed backtest results.
- **StrategyDetail_Page**: The Razor page at `Components/Pages/Strategies/StrategyDetail.razor` displaying strategy overview, versions, runs, and studies.
- **README**: The `README.md` file at the repository root.
- **CHANGELOG**: A `CHANGELOG.md` file at the repository root containing version history notes extracted from the README.

## Requirements

### Requirement 1: Remove CLI Project from Solution

**User Story:** As a developer, I want the CLI project completely removed from the solution, so that the codebase is focused on the Web UI as the sole entry point.

#### Acceptance Criteria

1. WHEN the solution is built, THE Solution_File SHALL NOT contain a project reference to `TradingResearchEngine.Cli`.
2. WHEN the repository is inspected, THE Web_Host SHALL be the only supported application host project present under `src/`.
3. THE Solution_File SHALL build successfully after the CLI_Project removal.
4. WHEN any remaining project file is inspected, THE Solution_File SHALL contain zero `ProjectReference` entries pointing to the CLI_Project.
5. IF the `samples/` directory exists solely for CLI scenario usage, THEN THE repository SHALL NOT contain the `samples/` directory.

### Requirement 2: Remove API Project from Solution

**User Story:** As a developer, I want the API project completely removed from the solution, so that there is no ambiguity about the supported entry point.

#### Acceptance Criteria

1. WHEN the solution is built, THE Solution_File SHALL NOT contain a project reference to `TradingResearchEngine.Api`.
2. WHEN the repository is inspected, THE Web_Host SHALL be the only supported application host project under `src/`, with no sibling API host project present.
3. THE Solution_File SHALL build successfully after the API_Project removal.
4. WHEN any remaining project file is inspected, THE Solution_File SHALL contain zero `ProjectReference` entries pointing to the API_Project.
5. WHEN the `src/` directory is listed, THE directory `src/TradingResearchEngine.Api/` SHALL NOT exist.
6. WHEN the `src/` directory is listed, THE directory `src/TradingResearchEngine.Cli/` SHALL NOT exist.

### Requirement 3: Remove Dead Code from CLI/API Removal

**User Story:** As a developer, I want all dead code made obsolete by the CLI and API removal cleaned up, so that the codebase contains no orphaned references.

#### Acceptance Criteria

1. WHEN the solution is searched for references to CLI-specific types, THE codebase SHALL contain zero compile-time references to types defined exclusively in the CLI_Project or API_Project.
2. WHEN integration tests reference `WebApplicationFactory<Program>` from the API_Project, THE Integration_Tests SHALL be updated to remove or replace those references.
3. THE Solution_File SHALL compile with zero errors after dead code removal.

### Requirement 4: README Overhaul for Web-Only Presentation

**User Story:** As a first-time user, I want the README to clearly present the application as a Web UI product with a concise Getting Started section, so that I can run the application within minutes.

#### Acceptance Criteria

1. THE README SHALL contain a "Getting Started" section within the first 40 lines that includes the commands `dotnet build`, `dotnet test`, and `dotnet run --project src/TradingResearchEngine.Web`.
2. THE README SHALL state the URL and port the application opens on after launch.
3. THE README SHALL state that the Dashboard is the landing page.
4. THE README SHALL describe what the user should expect to see on first launch.
5. THE README SHALL NOT contain any instructions to use CLI commands or API endpoints for running the application.
6. THE README SHALL contain a "Documentation" section linking each file in the `docs/` folder with a one-line description.
7. THE README SHALL contain a link to `CHANGELOG.md`.
8. WHEN version-history style notes exist inline in the README, THE CHANGELOG SHALL contain those notes extracted from the README.
9. THE README SHALL NOT reference `TradingResearchEngine.Cli` or `TradingResearchEngine.Api` as supported entry points.

### Requirement 5: CHANGELOG Creation

**User Story:** As a developer, I want version history notes extracted into a dedicated CHANGELOG file, so that the README stays focused on product overview and usage.

#### Acceptance Criteria

1. THE CHANGELOG SHALL exist at the repository root as `CHANGELOG.md`.
2. THE CHANGELOG SHALL contain all version-history and product-goals notes previously inline in the README.
3. THE CHANGELOG SHALL be organized chronologically with version identifiers.

### Requirement 6: Parameter Sweep Schema-Driven Selection

**User Story:** As a researcher, I want the Parameter Sweep page to offer a dropdown of typed parameters from the strategy schema, so that I do not need to remember or type parameter names manually.

#### Acceptance Criteria

1. WHEN a strategy version is selected on the Parameter_Sweep_Page, THE Parameter_Sweep_Page SHALL load the Strategy_Schema using the existing `IStrategySchemaProvider` service.
2. WHEN the Strategy_Schema is available, THE Parameter_Sweep_Page SHALL replace the free-text parameter name field with a dropdown populated from the schema parameter names.
3. IF the Strategy_Schema cannot be loaded for the selected strategy, THEN THE Parameter_Sweep_Page SHALL fall back to the existing free-text parameter name field.
4. WHEN a parameter is selected from the dropdown, THE Parameter_Sweep_Page SHALL display helper text containing the parameter description and sensitivity hint if metadata exists in the schema.
5. THE Parameter_Sweep_Page SHALL reuse the existing `StrategyParameterSchema` and `IStrategySchemaProvider` types without creating a parallel schema system.

### Requirement 7: Parameter Sweep Range Input

**User Story:** As a researcher, I want to specify sweep values as a range (low, high, increment) instead of manually typing comma-separated values, so that I can define large parameter grids efficiently.

#### Acceptance Criteria

1. WHEN a sweep row is configured, THE Parameter_Sweep_Page SHALL present three numeric fields: Low, High, and Increment.
2. WHEN the sweep is executed, THE Parameter_Sweep_Page SHALL generate the parameter value list from the Low, High, and Increment fields using the formula: values = [Low, Low+Increment, Low+2*Increment, ..., High].
3. IF Increment is zero or negative, THEN THE Parameter_Sweep_Page SHALL display a validation error and prevent execution.
4. IF Low is greater than High, THEN THE Parameter_Sweep_Page SHALL display a validation error and prevent execution.

### Requirement 8: Parameter Sweep Auto-Selection of Unused Parameters

**User Story:** As a researcher, I want new sweep rows to preselect a sensible unused parameter, so that I can build grids faster without manual selection.

#### Acceptance Criteria

1. WHEN a new sweep row is added and the Strategy_Schema is available, THE Parameter_Sweep_Page SHALL preselect the first parameter from the schema that is not already used in another sweep row.
2. IF all schema parameters are already used in existing rows, THEN THE Parameter_Sweep_Page SHALL leave the new row parameter selection empty.

### Requirement 9: Dashboard Last Sharpe Tile Contextual Display

**User Story:** As a researcher, I want the Last Sharpe KPI tile to show which strategy the Sharpe belongs to, so that I have immediate context without clicking through.

#### Acceptance Criteria

1. WHEN a completed run exists, THE Last_Sharpe_Tile SHALL display the strategy name from the associated `StrategyIdentity` if a matching strategy is found.
2. IF no `StrategyIdentity` match exists for the latest run, THEN THE Last_Sharpe_Tile SHALL display the strategy type string from `ScenarioConfig.StrategyType`.
3. IF no completed runs exist, THEN THE Last_Sharpe_Tile SHALL display "—" as the value and hide the strategy caption.

### Requirement 10: Dashboard Last Sharpe Tile Navigation

**User Story:** As a researcher, I want the Last Sharpe tile to be clickable and navigate to the relevant strategy or backtests page, so that I can quickly drill into details.

#### Acceptance Criteria

1. WHEN the Last_Sharpe_Tile is clicked and a strategy ID can be resolved from the latest run, THE Dashboard SHALL navigate to the strategy detail page for that strategy.
2. IF no strategy ID can be resolved from the latest run, THEN THE Dashboard SHALL navigate to the backtests history page.
3. IF no completed runs exist, THEN THE Last_Sharpe_Tile SHALL NOT be clickable.

### Requirement 11: Dashboard Robustness Warnings Use Most Recent Runs

**User Story:** As a researcher, I want the robustness warnings panel to check the 10 most recent completed runs by run date, so that warnings reflect current research activity rather than arbitrary storage order.

#### Acceptance Criteria

1. THE Robustness_Warnings_Panel SHALL evaluate robustness warnings against the 10 most recent completed runs ordered by the application's canonical recency ordering for completed runs (currently descending run date or equivalent existing recent-run ordering).
2. THE Robustness_Warnings_Panel SHALL reuse any existing recent-run projection or ordering logic already present in the Dashboard data loading.
3. THE Robustness_Warnings_Panel SHALL NOT evaluate runs that have a status other than Completed.

### Requirement 12: Extract ResultDetail Sub-Components

**User Story:** As a developer, I want the ResultDetail page split into smaller presentational components, so that the page is easier to maintain and reason about.

#### Acceptance Criteria

1. THE ResultDetail_Page SHALL extract a metrics panel component that renders the Tier 1 and Tier 2 metric cards.
2. THE ResultDetail_Page SHALL extract an equity curve panel component that renders the equity curve and drawdown charts.
3. THE ResultDetail_Page SHALL extract a trade log panel component that renders the trades table with pagination.
4. THE ResultDetail_Page SHALL extract a realism advisories panel component that renders the realism assumptions card and robustness warnings.
5. WHEN the ResultDetail_Page is rendered after refactoring, THE page SHALL produce identical visible output to the pre-refactoring version.
6. THE ResultDetail_Page SHALL retain its route declaration (`@page "/backtests/{Id}"`) and top-level data loading logic in the shell component.

### Requirement 13: Extract StrategyDetail Sub-Components

**User Story:** As a developer, I want the StrategyDetail page split into smaller tab/panel components, so that the 900+ line file is manageable.

#### Acceptance Criteria

1. THE StrategyDetail_Page SHALL extract an overview panel component containing the latest run summary, development stage, research progress, and quick actions.
2. THE StrategyDetail_Page SHALL extract a versions panel component containing the version parameters and execution configuration display.
3. THE StrategyDetail_Page SHALL extract a runs panel component containing the runs table and KPI summary.
4. THE StrategyDetail_Page SHALL extract a studies panel component containing the research study launch bar and studies table.
5. WHEN the StrategyDetail_Page is rendered after refactoring, THE page SHALL produce identical visible output to the pre-refactoring version.
6. THE StrategyDetail_Page SHALL retain its route declaration (`@page "/strategies/{StrategyId}"`) and top-level data loading logic in the shell component.
7. THE StrategyDetail_Page SHALL preserve all existing routes, tab behavior, and navigation links after refactoring.

### Requirement 14: Update Architecture Documentation

**User Story:** As a developer, I want all architecture and host-choice documentation updated to reflect the Web-only posture, so that there is no confusion about supported entry points.

#### Acceptance Criteria

1. WHEN the `docs/` folder is inspected, THE documentation SHALL NOT reference the CLI or API as active or supported host options.
2. THE README architecture diagram SHALL list only Core, Application, Infrastructure, Web, Benchmarks, UnitTests, and IntegrationTests as projects.
3. THE dependency rule documentation SHALL state `Core ← Application ← Infrastructure ← Web` without CLI or API.

### Requirement 15: Solution Build and Test Verification

**User Story:** As a developer, I want the solution to build and tests to pass after all changes, so that the refactoring does not introduce regressions.

#### Acceptance Criteria

1. WHEN `dotnet build` is run against the solution, THE build SHALL complete with zero errors.
2. WHEN `dotnet test` is run against the solution, THE test suite SHALL pass or any failures SHALL be documented as pre-existing issues unrelated to this change.
3. THE Web_Host SHALL be the only runnable host project after the changes.

### Requirement 16: Preserve Backtesting Engine Logic

**User Story:** As a researcher, I want the backtesting engine logic to remain unchanged, so that all existing research results remain valid.

#### Acceptance Criteria

1. THE Core project SHALL have zero source file modifications as part of this feature.
2. THE Application project SHALL have zero modifications to engine logic, research workflow logic, or domain calculations as part of this feature.
3. WHEN existing unit tests and property-based tests are run, THE tests SHALL produce the same pass/fail results as before the changes.

### Requirement 17: No Lingering CLI/API References

**User Story:** As a user, I want zero remaining mentions of CLI or API as supported entry points anywhere in user-facing documentation or code comments, so that the product identity is unambiguous.

#### Acceptance Criteria

1. WHEN the repository is searched for the strings "TradingResearchEngine.Cli" or "TradingResearchEngine.Api", THE search SHALL return zero results in user-facing documentation, README, or docs files.
2. WHEN code comments reference CLI or API usage instructions, THE comments SHALL be removed or updated to reference the Web UI.
3. THE `docs/UI-Planning-Specification.md` SHALL be updated to remove references to CLI as a parallel host option.
