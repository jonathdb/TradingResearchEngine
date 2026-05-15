# Requirements Document

## Introduction

This document specifies the requirements for a comprehensive, multi-PR implementation effort across the TradingResearchEngine project. The effort spans 8 PR gates covering walk-forward correctness, indicator fixes, performance optimization, configuration canonicalization, persistence hardening, repository cleanup, research analytics expansion, and engine capability expansion. Each PR gate represents a coherent, reviewable unit of work that must build, pass tests, and align documentation before the next gate begins.

## Glossary

- **Engine**: The TradingResearchEngine event-driven backtesting engine (Core layer)
- **Walk_Forward_Workflow**: The Application-layer orchestration that splits historical data into sequential in-sample/out-of-sample windows, optimizes parameters in-sample, and validates out-of-sample
- **Parameter_Grid**: A structured definition of parameter names, ranges, and step sizes used for in-sample optimization within walk-forward and sweep workflows
- **In_Sample_Window**: The portion of a walk-forward data window used for parameter optimization
- **Out_Of_Sample_Window**: The portion of a walk-forward data window used for validation of the in-sample-selected parameters
- **Research_Checklist**: A multi-item checklist tracking the completeness and quality of a research workflow before final validation
- **Indicator_Catalog**: The `SkenderIndicatorCatalog` registry mapping indicator names to factory functions that produce indicator instances
- **Strategy_Builder**: The guided UI wizard for constructing trading strategies from templates and indicator selections
- **Realism_Profile**: A named configuration preset (FastResearch, StandardBacktest, BrokerConservative) controlling slippage, commission, and fill assumptions
- **Portfolio**: The Core-layer mutable state object tracking positions, cash, and equity during a backtest run
- **Monte_Carlo_Workflow**: The Application-layer workflow performing stochastic robustness analysis via trade resampling or path simulation
- **CPCV_Workflow**: The Combinatorial Purged Cross-Validation workflow for unbiased backtest evaluation
- **Parameter_Perturbation_Workflow**: The workflow that jitters strategy parameters around a baseline to assess sensitivity
- **Scenario_Config**: The canonical configuration record defining all parameters for a single backtest run
- **Strategy_Registry**: The Application-layer singleton that maps strategy names to implementation types via assembly scanning
- **Gemini_AI_Service**: The Infrastructure-layer service calling the Gemini generative AI API for strategy assistance
- **Job_System**: The background job execution infrastructure managing long-running research workflows
- **SQLite_Index**: The Infrastructure-layer SQLite database providing O(log n) lookups over persisted entities
- **JSON_Store**: The `JsonFileRepository<T>` providing file-based persistence of domain entities
- **Paper_Trading_Service**: The service polling live market data for simulated paper-trade execution
- **Comparison_Report**: A durable artifact (Markdown or HTML) comparing multiple backtest runs or strategies
- **MAE**: Maximum Adverse Excursion — the largest unrealized loss during a trade's lifetime
- **MFE**: Maximum Favorable Excursion — the largest unrealized gain during a trade's lifetime
- **Expression_Compiler**: The component parsing and compiling composite strategy conditions from string expressions
- **Multi_Timeframe_Strategy**: A strategy consuming price data from more than one timeframe simultaneously
- **Export_Validator**: The component validating generated Pine Script or MQL code for structural and syntactic correctness
- **OptimizationObjective**: A configurable metric (Sharpe, CAGR, or MAR) used by walk-forward and sweep workflows to rank candidate parameter combinations during in-sample optimization
- **PR_Gate**: A coherent pull request boundary that must satisfy build, test, documentation, and review criteria before the next gate begins

## Requirements

### Requirement 1: Walk-Forward In-Sample Optimization

**User Story:** As a quantitative researcher, I want walk-forward to perform real in-sample parameter optimization with grid support, so that out-of-sample validation uses genuinely optimized parameters rather than a single fixed configuration.

#### Acceptance Criteria

1. WHEN a walk-forward run is initiated with a Parameter_Grid, THE Walk_Forward_Workflow SHALL evaluate all parameter combinations within each In_Sample_Window and select the combination producing the highest value of the configured OptimizationObjective
2. WHEN the best in-sample parameter combination is selected, THE Walk_Forward_Workflow SHALL apply that combination to the corresponding Out_Of_Sample_Window for validation
3. IF an empty or invalid Parameter_Grid is provided, THEN THE Walk_Forward_Workflow SHALL return a structured validation error identifying the invalid grid fields
4. WHEN multiple In_Sample_Windows are processed, THE Walk_Forward_Workflow SHALL independently optimize parameters within each window without leaking information across windows
5. THE Walk_Forward_Workflow SHALL expose the selected in-sample parameters and their optimization metric for each window in the result output
6. THE Walk_Forward_Workflow SHALL expose a configurable OptimizationObjective on its options model with a default value of Sharpe
7. THE Walk_Forward_Workflow SHALL support CAGR and MAR as explicit alternative OptimizationObjective values selectable by the user
8. IF the chosen OptimizationObjective is undefined for a candidate result (e.g., insufficient data to compute Sharpe), THEN THE Walk_Forward_Workflow SHALL exclude that candidate from ranking and provide a structured explanation identifying why the candidate was excluded
9. THE Walk_Forward_Workflow SHALL NOT silently fall through to a different objective when the configured OptimizationObjective is undefined for a candidate

### Requirement 2: Walk-Forward Pre-Run Validation

**User Story:** As a quantitative researcher, I want impossible walk-forward configurations to be blocked before execution starts, so that I do not waste time on runs that cannot produce valid results.

#### Acceptance Criteria

1. WHEN a walk-forward configuration is submitted, THE Walk_Forward_Workflow SHALL validate that the data range accommodates at least one complete In_Sample_Window and Out_Of_Sample_Window pair given the step size
2. IF the data range is insufficient for any valid window, THEN THE Walk_Forward_Workflow SHALL reject the run with a structured error stating the minimum required data length
3. WHEN validation succeeds, THE Walk_Forward_Workflow SHALL report the expected window count to the caller before execution begins
4. IF the configuration produces fewer than two windows, THEN THE Walk_Forward_Workflow SHALL emit a warning indicating limited statistical significance

### Requirement 3: Irreversible Action Confirmation for Final Validation

**User Story:** As a quantitative researcher, I want explicit confirmation before consuming the out-of-sample test set in final validation, so that I do not accidentally invalidate my holdout data.

#### Acceptance Criteria

1. WHEN the user initiates final validation that consumes the test set, THE Engine SHALL require explicit confirmation before proceeding with the irreversible action
2. THE Engine SHALL display a clear explanation of the consequences of consuming the test set before requesting confirmation
3. WHILE the test set has already been consumed for a given strategy version, THE Engine SHALL disable or clearly relabel the final validation action to indicate it is no longer available
4. IF the user declines confirmation, THEN THE Engine SHALL cancel the final validation without modifying any state

### Requirement 4: Research Checklist as Active Workflow Guide

**User Story:** As a quantitative researcher, I want the research checklist to actively guide my workflow and surface incomplete items prominently, so that I understand what remains before final validation.

#### Acceptance Criteria

1. THE Research_Checklist SHALL surface incomplete or failing items with prominent visual indicators distinguishing them from completed items
2. WHEN a checklist item is incomplete, THE Research_Checklist SHALL provide a direct link or navigation path to the relevant workflow or page that addresses the item
3. WHEN confidence is assessed as low, THE Research_Checklist SHALL display an explanation of why confidence is low rather than only a numeric score or label
4. WHILE the research checklist contains incomplete critical items, THE Engine SHALL display a warning during the final validation experience indicating that gating criteria are not met
5. THE Research_Checklist SHALL integrate its state into the final validation flow so that the user sees checklist status before confirming irreversible actions

### Requirement 5: Indicator Catalog Completeness

**User Story:** As a strategy builder user, I want every indicator in the catalog to produce a valid indicator instance, so that I never encounter silent null results when selecting indicators.

#### Acceptance Criteria

1. THE Indicator_Catalog SHALL return a valid, fully-functional indicator instance for every entry advertised in the catalog
2. IF an indicator cannot be implemented, THEN THE Indicator_Catalog SHALL remove the entry from the catalog and the Strategy_Builder SHALL not advertise the indicator to users
3. WHEN an indicator factory is invoked, THE Indicator_Catalog SHALL produce a non-null result or throw a descriptive exception identifying the unsupported indicator
4. THE Strategy_Builder SHALL only display indicators that are present and functional in the Indicator_Catalog

### Requirement 6: Remove Obsolete LongOnlyGuard Documentation Drift

**User Story:** As a developer, I want documentation and comments to accurately reflect the current short-selling support status, so that I do not encounter misleading references to removed or inactive guards.

#### Acceptance Criteria

1. WHEN the LongOnlyGuard is no longer active runtime behavior, THE Engine SHALL remove or rewrite all XML docs, comments, and spec text that reference LongOnlyGuard as active runtime behavior
2. THE Engine SHALL update all test assertions and documentation to reflect the actual V6+ bidirectional execution support
3. IF LongOnlyGuard code remains for backward compatibility, THEN THE Engine SHALL mark it clearly as deprecated with a reference to the replacement mechanism

### Requirement 7: Beginner-Mode Realism Defaults

**User Story:** As a beginner user, I want the strategy builder to default to realistic execution assumptions, so that my initial backtest results are not misleadingly optimistic.

#### Acceptance Criteria

1. WHEN a user creates a strategy through the beginner flow, THE Strategy_Builder SHALL default to the StandardBacktest Realism_Profile or an equivalent realistic configuration
2. THE Strategy_Builder SHALL display explanatory text describing what the default realism settings mean for backtest accuracy
3. WHILE the beginner flow is active, THE Strategy_Builder SHALL keep advanced realism overrides accessible but not prominently displayed
4. THE Strategy_Builder SHALL not default to FastResearch or zero-cost profiles in beginner mode

### Requirement 8: Portfolio Hot-Path Allocation Reduction

**User Story:** As a performance-sensitive researcher running large sweeps, I want the Portfolio to avoid repeated allocations on hot paths, so that backtest throughput is maximized.

#### Acceptance Criteria

1. THE Portfolio SHALL avoid recreating position snapshot collections on every access to Positions or ShortPositions properties
2. THE Portfolio SHALL provide O(1) access to OpenPositionCount without repeated enumeration
3. WHEN position state changes, THE Portfolio SHALL update cached snapshots incrementally rather than rebuilding from scratch
4. THE Portfolio SHALL maintain identical observable behavior and correctness after optimization, verified by existing property-based tests

### Requirement 9: Nested Parallelism Oversubscription Elimination

**User Story:** As a researcher running combined walk-forward and sweep workflows, I want concurrency to be bounded globally, so that the system does not oversubscribe CPU resources through nested parallelism.

#### Acceptance Criteria

1. THE Engine SHALL enforce a global or hierarchical concurrency budget preventing nested parallel workflows from exceeding available CPU resources
2. WHEN walk-forward and parameter sweep execute concurrently, THE Engine SHALL coordinate their parallelism to stay within the configured concurrency limit
3. THE Engine SHALL keep each individual backtest run isolated regardless of the concurrency scheduling approach
4. THE Engine SHALL produce logically equivalent final outputs for seeded workflows regardless of the concurrency scheduling order
5. THE Engine SHALL reproduce seeded stochastic decisions (RNG paths, trade sequences) identically given the same seed regardless of parallelism configuration
6. THE Engine SHALL produce final numeric outputs that are equal or within a documented floating-point tolerance where order-dependent aggregation is unavoidable
7. THE Engine SHALL NOT require bit-for-bit serialized identity of JSON or collection ordering unless ordering is part of the public contract

### Requirement 10: Monte Carlo Workflow Parallelization

**User Story:** As a researcher running Monte Carlo robustness analysis, I want simulations to execute in parallel with bounded concurrency, so that large simulation counts complete faster.

#### Acceptance Criteria

1. THE Monte_Carlo_Workflow SHALL execute simulations in parallel using bounded concurrency
2. WHEN a seed is provided, THE Monte_Carlo_Workflow SHALL produce logically equivalent results to sequential execution with the same seed, with final numeric outputs equal or within a documented floating-point tolerance where order-dependent aggregation is unavoidable
3. WHEN a seed is provided, THE Monte_Carlo_Workflow SHALL reproduce identical RNG paths and trade sequences regardless of parallelism scheduling
4. THE Monte_Carlo_Workflow SHALL preserve existing algorithm semantics including block bootstrap behavior
5. THE Monte_Carlo_Workflow SHALL report accurate progress during parallel execution

### Requirement 11: CPCV Workflow Parallelization

**User Story:** As a researcher running CPCV studies, I want fold evaluations to execute in parallel with bounded concurrency, so that large fold counts complete faster.

#### Acceptance Criteria

1. THE CPCV_Workflow SHALL execute fold evaluations in parallel using bounded concurrency
2. THE CPCV_Workflow SHALL preserve correctness of progress reporting during parallel execution
3. THE CPCV_Workflow SHALL aggregate results correctly regardless of fold completion order, producing logically equivalent final outputs independent of scheduling
4. THE CPCV_Workflow SHALL avoid shared mutable state between concurrent fold evaluations
5. THE CPCV_Workflow SHALL produce final numeric outputs that are equal or within a documented floating-point tolerance where order-dependent aggregation is unavoidable

### Requirement 12: Parameter Perturbation Workflow Parallelization

**User Story:** As a researcher running parameter perturbation analysis, I want perturbation runs to execute in parallel, so that sensitivity analysis completes faster.

#### Acceptance Criteria

1. THE Parameter_Perturbation_Workflow SHALL execute perturbation runs in parallel using bounded concurrency
2. WHEN a seed is provided, THE Parameter_Perturbation_Workflow SHALL reproduce identical jitter values (RNG paths) regardless of parallelism scheduling
3. WHEN a seed is provided, THE Parameter_Perturbation_Workflow SHALL produce logically equivalent final outputs to sequential execution with the same seed, with numeric values equal or within a documented floating-point tolerance where order-dependent aggregation is unavoidable
4. THE Parameter_Perturbation_Workflow SHALL report the correct total run count in results

### Requirement 13: Progress Estimation Accuracy

**User Story:** As a researcher monitoring long-running workflows, I want progress estimation to account for data provider characteristics, so that progress bars reflect actual completion more accurately.

#### Acceptance Criteria

1. THE Engine SHALL use provider-aware or interval-aware bar-count estimation for progress reporting
2. WHEN a data provider can supply an estimated bar count, THE Engine SHALL use that estimate rather than a generic default
3. THE Engine SHALL keep progress estimation lightweight without forcing expensive full data preloading
4. THE Engine SHALL update progress estimates as actual bar counts become known during execution

### Requirement 14: ScenarioConfig Canonicalization

**User Story:** As a developer, I want a single canonical configuration schema, so that dual-schema drift does not cause inconsistencies between load and runtime behavior.

#### Acceptance Criteria

1. THE Engine SHALL normalize all loaded configurations to one canonical Scenario_Config shape before validation and runtime use
2. WHEN a legacy-format configuration is loaded, THE Engine SHALL transform it to the canonical shape in memory transparently without modifying the legacy file on disk
3. THE Engine SHALL persist configurations in the canonical shape when an explicit save operation is performed
4. THE Engine SHALL validate canonical configurations with a single validation path rather than duplicated validation logic
5. THE Engine SHALL NOT silently rewrite legacy configuration files on disk during a load operation
6. WHEN a user initiates an explicit save or migration action on a legacy configuration, THE Engine SHALL write the canonical shape to disk at that point

### Requirement 15: Unified Strategy Construction

**User Story:** As a developer, I want all strategy instantiation to flow through one consistent factory mechanism, so that construction behavior is predictable and testable.

#### Acceptance Criteria

1. THE Strategy_Registry SHALL serve as the single entry point for runtime strategy construction
2. THE Engine SHALL not use reflection-based divergence paths that bypass the Strategy_Registry for strategy instantiation
3. WHEN a strategy is constructed, THE Engine SHALL apply consistent initialization regardless of the entry point (UI builder, config file, API request)
4. THE Engine SHALL verify at startup that all registered strategies can be instantiated without error

### Requirement 16: Typed Provider Configuration

**User Story:** As a developer, I want data provider options accessed through typed configuration rather than string-key dictionaries, so that configuration errors are caught at compile time.

#### Acceptance Criteria

1. THE Engine SHALL expose data provider configuration through strongly-typed option classes bound via `IOptions<T>`
2. THE Engine SHALL replace scattered string-key dictionary access in DataHandler, Walk_Forward_Workflow, CPCV_Workflow, and data providers with typed property access
3. WHEN a required configuration value is missing, THE Engine SHALL produce a compile-time or startup-time error rather than a runtime KeyNotFoundException
4. THE Engine SHALL maintain backward compatibility at the JSON configuration ingestion boundary for existing configuration files

### Requirement 17: AI Call Timeout and Cancellation

**User Story:** As a user interacting with the AI strategy assistant, I want AI calls to have configurable timeouts, so that a slow or unresponsive AI service does not hang the application indefinitely.

#### Acceptance Criteria

1. THE Gemini_AI_Service SHALL enforce a configurable timeout on all outbound AI API calls
2. THE Gemini_AI_Service SHALL use linked CancellationTokens to propagate cancellation from the caller through to the HTTP request
3. IF an AI call exceeds the configured timeout, THEN THE Gemini_AI_Service SHALL cancel the request and return a descriptive timeout error
4. THE Gemini_AI_Service SHALL preserve existing retry behavior for transient failures while respecting the per-call timeout

### Requirement 18: Job Retry and Failure Handling

**User Story:** As a researcher running background jobs, I want jobs to have explicit retry policies and final-failure states, so that transient errors are retried and permanent failures are surfaced clearly.

#### Acceptance Criteria

1. THE Job_System SHALL implement a configurable retry policy with backoff for transient failures
2. THE Job_System SHALL distinguish transient failures (network timeout, temporary unavailability) from terminal failures (invalid configuration, missing data)
3. IF a job exhausts its retry budget, THEN THE Job_System SHALL transition the job to a final-failure state with a sanitized user-visible error message
4. THE Job_System SHALL not enter infinite retry loops regardless of failure type
5. THE Job_System SHALL log structured diagnostics for each retry attempt and final failure

### Requirement 19: SQLite/JSON Consistency Reconciliation

**User Story:** As a system operator, I want the persistence layer to detect and reconcile divergence between SQLite index and JSON store, so that data integrity is maintained across restarts.

#### Acceptance Criteria

1. WHEN the application starts, THE Engine SHALL verify consistency between the SQLite_Index and JSON_Store for all indexed entity types
2. IF a mismatch is detected between SQLite_Index and JSON_Store, THEN THE Engine SHALL reconcile the divergence by treating the JSON_Store as the source of truth
3. WHEN reconciliation occurs, THE Engine SHALL log structured diagnostics identifying the mismatched entities and the corrective action taken
4. THE Engine SHALL complete reconciliation without data loss from the JSON_Store

### Requirement 20: Configurable Paper-Trading Polling

**User Story:** As a paper-trading user, I want the polling interval to be configurable, so that I can balance responsiveness against resource usage.

#### Acceptance Criteria

1. THE Paper_Trading_Service SHALL read its polling interval from a configurable option bound via `IOptions<T>`
2. THE Paper_Trading_Service SHALL use a sensible default polling interval when no configuration is provided
3. WHEN the polling interval configuration changes, THE Paper_Trading_Service SHALL apply the new interval without requiring application restart where feasible
4. THE Paper_Trading_Service SHALL validate that the configured interval is within acceptable bounds (not zero, not excessively large)

### Requirement 21: Repository Cleanup — Prompt History and AI Artifacts

**User Story:** As a developer, I want the repository to contain only production-relevant prompt files, so that archival artifacts do not clutter the working tree.

#### Acceptance Criteria

1. THE Engine repository SHALL retain only prompt files actively used by production code in the Prompts directory
2. WHEN prompt files are removed or relocated, THE Engine SHALL update all file path references in code and configuration
3. THE Engine SHALL not break any runtime behavior by removing archival prompt-engineering artifacts

### Requirement 22: Repository Cleanup — Obsolete CLI/API/Web Transition Leftovers

**User Story:** As a developer, I want obsolete samples, docs, and references from prior architecture transitions removed, so that the repository reflects the current system accurately.

#### Acceptance Criteria

1. THE Engine repository SHALL remove or relocate obsolete assets referencing removed entry points (old CLI modes, deprecated API patterns)
2. WHEN samples are retained intentionally, THE Engine SHALL document the reason for their retention
3. THE Engine SHALL update README, CHANGELOG, and docs to reflect only current architecture and entry points

### Requirement 23: Documentation and Spec Alignment

**User Story:** As a developer, I want all documentation, specs, and code comments to reflect implemented reality, so that I can trust documentation as accurate.

#### Acceptance Criteria

1. WHEN implementation changes are made, THE Engine SHALL update affected README, CHANGELOG, docs, spec design documents, requirements documents, and task lists in the same PR
2. THE Engine SHALL remove stale XML doc comments that no longer reflect the code they document
3. THE Engine SHALL mark completed tasks in `.kiro/specs/*/tasks.md` files when the corresponding implementation is merged
4. THE Engine SHALL not retain comments describing planned-but-not-implemented behavior without a clear "TODO" or "PLANNED" prefix

### Requirement 24: Expanded Monte Carlo Simulation Modes

**User Story:** As a quantitative researcher, I want Monte Carlo analysis to support simulation modes beyond trade-order resampling, so that I can assess robustness from multiple statistical perspectives.

#### Acceptance Criteria

1. THE Monte_Carlo_Workflow SHALL support an explicit simulation mode selection in its configuration and domain model
2. THE Monte_Carlo_Workflow SHALL preserve existing trade-resample and block-bootstrap modes as selectable options
3. THE Monte_Carlo_Workflow SHALL implement at least one additional simulation mode (return-series or path-based simulation) end-to-end including result generation and reporting
4. THE Monte_Carlo_Workflow SHALL clearly explain the statistical differences between available modes in UI presentation and documentation
5. WHEN a simulation mode is selected, THE Monte_Carlo_Workflow SHALL execute only that mode's algorithm without mixing approaches

### Requirement 25: Enriched Walk-Forward Analytics

**User Story:** As a quantitative researcher, I want richer walk-forward analytics including OOS profitability rates, concatenated equity curves, and parameter stability metrics, so that I can assess strategy robustness across time more thoroughly.

#### Acceptance Criteria

1. THE Walk_Forward_Workflow SHALL compute and report the percentage of out-of-sample windows that are profitable
2. THE Walk_Forward_Workflow SHALL produce a concatenated out-of-sample equity curve combining all OOS window results in chronological order
3. THE Walk_Forward_Workflow SHALL compute a parameter drift or stability summary indicating how much optimal parameters change across successive windows
4. THE Walk_Forward_Workflow SHALL include the enriched metrics in both programmatic result objects and visual presentation
5. THE Walk_Forward_Workflow SHALL expose a configurable OptimizationObjective on its options model with a default value of Sharpe, supporting CAGR and MAR as explicit alternatives
6. IF the chosen OptimizationObjective is undefined for a candidate result, THEN THE Walk_Forward_Workflow SHALL exclude that candidate from ranking with a structured explanation rather than falling through to a different objective

### Requirement 26: Trade Anatomy Analytics

**User Story:** As a quantitative researcher, I want trade-level analytics including MAE, MFE, and duration distributions, so that I can understand trade behavior beyond aggregate metrics.

#### Acceptance Criteria

1. THE Engine SHALL compute Maximum Adverse Excursion (MAE) for each closed trade when intra-trade price data is available
2. THE Engine SHALL compute Maximum Favorable Excursion (MFE) for each closed trade when intra-trade price data is available
3. THE Engine SHALL compute trade duration and produce duration distribution analytics
4. THE Engine SHALL present trade anatomy analytics in the results UI with appropriate visualizations
5. IF intra-trade price data is not available for MAE/MFE computation, THEN THE Engine SHALL indicate that trade anatomy metrics are unavailable rather than producing incorrect values

### Requirement 27: Correlation-Aware Portfolio Constraints

**User Story:** As a portfolio researcher, I want correlation controls in PortfolioRiskConfig to be enforced at runtime, so that portfolio construction respects diversification constraints rather than only reporting them post-analysis.

#### Acceptance Criteria

1. WHEN PortfolioRiskConfig specifies correlation constraints, THE Engine SHALL enforce those constraints during portfolio execution and position selection
2. THE Engine SHALL reject or defer new positions that would violate correlation constraints rather than allowing entry and reporting violations after the fact
3. THE Engine SHALL log structured diagnostics when a position is rejected due to correlation constraints
4. THE Engine SHALL update UI text and documentation to reflect that correlation controls are enforced at runtime rather than advisory-only

### Requirement 28: Persistent Comparison Report Generation

**User Story:** As a quantitative researcher, I want to generate durable comparison reports for strategy or run comparisons, so that I can archive and share comparison results outside the application.

#### Acceptance Criteria

1. THE Engine SHALL generate comparison reports in Markdown as the primary durable artifact format
2. THE Engine SHALL include key metrics, equity curves, and summary statistics in comparison reports
3. WHEN a comparison report is generated, THE Engine SHALL persist the Markdown artifact to the configured output location
4. THE Engine SHALL integrate comparison report generation into the comparison UI as an accessible action
5. WHERE HTML export is enabled as an optional secondary format, THE Engine SHALL generate an HTML comparison report in addition to the Markdown report
6. THE Engine SHALL use Markdown as the primary format because it is diffable, versionable, and lightweight

### Requirement 29: AI Refinement Loop with Backtest Context

**User Story:** As a researcher refining AI-generated strategies, I want the AI assistant to automatically receive the latest backtest metrics when refining, so that refinement suggestions are grounded in actual performance data.

#### Acceptance Criteria

1. WHEN a strategy refinement is initiated via the AI assistant, THE Gemini_AI_Service SHALL automatically include the latest relevant backtest metrics in the refinement prompt
2. THE Gemini_AI_Service SHALL summarize backtest metrics concisely to keep prompts within token budget constraints
3. THE Gemini_AI_Service SHALL include metrics that are actionable for refinement (Sharpe, drawdown, win rate, trade count, K-Ratio) rather than raw data dumps
4. THE Engine SHALL make the inclusion of backtest context visible to the user in the refinement UI flow

### Requirement 30: Large Sweep Result Usability

**User Story:** As a researcher running large parameter sweeps, I want results to remain responsive and navigable regardless of result count, so that large sweeps do not render the UI unusable.

#### Acceptance Criteria

1. WHEN sweep results exceed a display threshold, THE Engine SHALL use paging, virtualization, or an equivalent technique to maintain UI responsiveness
2. THE Engine SHALL preserve chart and summary views for large sweep results without loading all individual results into the DOM simultaneously
3. THE Engine SHALL allow filtering and sorting of large sweep result sets without full client-side data loading
4. THE Engine SHALL display sweep summary statistics regardless of result count

### Requirement 31: Comparison Page Consolidation

**User Story:** As a user navigating comparison features, I want a single coherent comparison flow rather than multiple overlapping comparison pages, so that navigation is clear and predictable.

#### Acceptance Criteria

1. THE Engine SHALL provide one canonical comparison route and page for strategy/run comparisons
2. IF multiple comparison components remain, THEN THE Engine SHALL clearly differentiate their roles in navigation, route naming, and page titles
3. THE Engine SHALL remove obsolete or dead comparison routes that are no longer reachable through navigation
4. THE Engine SHALL update documentation and navigation to reflect the consolidated comparison flow

### Requirement 32: Multi-Timeframe Strategy Support

**User Story:** As a quantitative researcher, I want to build strategies that consume data from multiple timeframes simultaneously, so that I can implement strategies that use higher-timeframe context for lower-timeframe decisions.

#### Acceptance Criteria

1. THE Engine SHALL extend Scenario_Config to support specification of secondary timeframe data sources alongside the primary timeframe
2. THE Engine SHALL provide event plumbing that delivers bars from multiple timeframes to the strategy in correct chronological order
3. THE Engine SHALL implement at least one concrete reference strategy demonstrating multi-timeframe execution end-to-end
4. THE Engine SHALL validate that all specified timeframe data sources are available before starting execution
5. IF a secondary timeframe data source is unavailable, THEN THE Engine SHALL return a structured validation error identifying the missing source

### Requirement 33: Export Code Validation

**User Story:** As a researcher exporting strategies to Pine Script or MQL, I want exported code to be validated for structural and syntactic correctness, so that I receive immediate feedback on export quality rather than discovering errors in the target platform.

#### Acceptance Criteria

1. THE Export_Validator SHALL validate generated Pine Script and MQL exports for structural correctness before presenting them to the user
2. THE Export_Validator SHALL use robust structural and syntax heuristics at minimum, with deeper validation where practical without unsafe external dependencies
3. IF export validation fails, THEN THE Export_Validator SHALL report specific validation errors identifying the problematic sections
4. THE Export_Validator SHALL include regression tests covering known-good and known-bad export patterns

### Requirement 34: Expression Compiler Negative Testing

**User Story:** As a developer, I want the ExpressionCompiler to have comprehensive negative tests for malformed inputs, so that error handling is verified and regressions are caught.

#### Acceptance Criteria

1. THE Expression_Compiler SHALL have unit tests covering malformed composite condition inputs including missing operators, unbalanced parentheses, invalid identifiers, and empty expressions
2. WHEN a malformed expression is provided, THE Expression_Compiler SHALL return a descriptive error rather than throwing an unhandled exception or producing incorrect output
3. THE Expression_Compiler SHALL not produce a valid compiled result from any malformed input covered by negative tests

### Requirement 35: PR Gate Process Compliance

**User Story:** As a project maintainer, I want each PR gate to satisfy explicit merge criteria before the next gate begins, so that quality is maintained incrementally and regressions are caught early.

#### Acceptance Criteria

1. WHEN a PR gate is completed, THE Engine SHALL build cleanly with zero errors and zero warnings treated as errors
2. WHEN a PR gate is completed, THE Engine SHALL have all relevant tests passing including new tests added in that gate
3. WHEN a PR gate modifies behavior, THE Engine SHALL update affected documentation, specs, and comments within the same gate
4. WHEN a PR gate removes or replaces functionality, THE Engine SHALL remove obsolete code, comments, and documentation for that functionality within the same gate
5. THE Engine SHALL maintain backward compatibility for persisted data formats unless the PR gate explicitly authorizes a migration
