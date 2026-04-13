# V5 Requirements — Engine Usability & Quant Upgrades

## Introduction

V5 evolves TradingResearchEngine across four dimensions simultaneously: strategy creation usability, backtesting/research workflow ergonomics, quant-trading robustness and realism, and API/domain model quality. The engine already has strong architecture (clean layered boundaries), correct V2 event processing, broad metrics, and rich research workflows. V5 addresses the remaining friction: strategy creation is too code-centric, `ScenarioConfig` is a god object, API endpoints lack ergonomic discovery, long-running jobs need better lifecycle management, long-only assumptions are too embedded, and cross-asset portfolio support is limited.

V5 is scoped as an MVP. Advanced no-code/DSL strategy authoring, full CPCV implementation, and multi-currency portfolio tracking are explicitly deferred to V5.1/V6 roadmap. All changes are additive and backwards-compatible unless explicitly versioned. The clean architecture boundary `Core ← Application ← Infrastructure ← {Cli, Api}` is preserved throughout.

## Glossary

- **Strategy_Builder**: The multi-step guided wizard for creating and configuring strategies (Application + Web layers)
- **Strategy_Schema**: A typed parameter descriptor for an `IStrategy` implementation, exposing parameter names, types, defaults, constraints, and metadata
- **Config_Draft**: An in-progress strategy configuration being assembled in the builder, persisted via `IRepository<ConfigDraft>` before promotion to a `StrategyVersion`
- **Resolved_Config**: The final effective configuration after applying defaults, presets, and overrides — the exact values the engine will use
- **Preflight_Validator**: An Application-layer service that validates a complete configuration before engine execution, returning structured findings with severity levels
- **Research_Summary_Rail**: A persistent side panel in the builder UI showing live configuration state, validation status, and recommended next actions
- **Config_Preset**: A named, reusable set of configuration defaults (e.g. "Fast Idea Check", "Research-Grade Validation")
- **Job**: An async execution unit for long-running backtests and studies, with lifecycle (queued → running → completed/failed/cancelled)
- **Progress_Reporter**: An Application-layer interface for reporting execution progress (current/total, stage, elapsed time)
- **Direction_Enum**: Core enum currently `{ Long, Flat }`; V5 prepares extensibility for `Short` without breaking existing code
- **ScenarioConfig**: The existing monolithic configuration record in Core; V5 introduces sub-object decomposition with backward-compatible adapter
- **DataConfig**: V5 sub-object for data provider settings (provider type, options, timeframe, date range)
- **StrategyConfig**: V5 sub-object for strategy type and parameters
- **RiskConfig**: V5 sub-object for risk parameters, position sizing, and exposure limits
- **ExecutionConfig**: V5 sub-object for realism profile, slippage, commission, session, and fill mode settings
- **ResearchConfig**: V5 sub-object for research workflow type, options, and sealed test set configuration
- **Parameter_Schema_Provider**: An Application-layer service that returns typed `StrategyParameterSchema` for any registered strategy

---

## AREA A — Strategy Creation Experience

### Requirement 1 — Typed Strategy Parameter Schema

**User Story:** As a researcher, I want each strategy to expose a typed parameter schema with names, types, defaults, constraints, and metadata, so that the builder, API, and CLI can render informed parameter editors without hard-coding knowledge of each strategy.

#### Acceptance Criteria

1. THE Application layer SHALL define a `StrategyParameterSchema` record with fields: `Name` (string), `DisplayName` (string), `Type` (string: int/decimal/bool/enum), `DefaultValue` (object), `IsRequired` (bool), `Min` (object, nullable), `Max` (object, nullable), `EnumChoices` (string array, nullable), `Description` (string), `SensitivityHint` (enum: Low/Medium/High), `Group` (string: Signal/Entry/Exit/Risk/Filters/Execution), `IsAdvanced` (bool), `DisplayOrder` (int).
2. THE Application layer SHALL define an `IStrategySchemaProvider` interface with method `GetSchema(string strategyName)` returning `IReadOnlyList<StrategyParameterSchema>`.
3. THE `StrategyRegistry` SHALL be extended with a `GetParameterSchema(string strategyName)` method that inspects strategy constructors and optional `[ParameterMeta]` attributes to build the schema.
4. WHEN a strategy parameter has a `[ParameterMeta]` attribute, THEN the schema provider SHALL use the attribute values for display name, description, sensitivity hint, group, and advanced flag.
5. WHEN a strategy parameter lacks a `[ParameterMeta]` attribute, THEN the schema provider SHALL fall back to constructor parameter name, inferred type, and default value — the schema is never empty for a registered strategy.
6. ALL 6 built-in strategies SHALL have `[ParameterMeta]` attributes on all constructor parameters.
7. THE `StrategyDescriptor` record SHALL gain an optional `IReadOnlyList<StrategyParameterSchema>? ParameterSchemas` field (trailing parameter, default null) for backward-compatible deserialization.

---

### Requirement 2 — Strategy Template and Builder Support

**User Story:** As a researcher, I want to start strategy creation from a template organized by strategy family, with presets for common configurations, so that I can begin research quickly without constructing configuration from scratch.

#### Acceptance Criteria

1. THE `StrategyTemplate` record SHALL gain optional fields: `FamilyPresets` (dictionary of preset name → parameter overrides, nullable) and `DifficultyLevel` (enum: Beginner/Intermediate/Advanced, default Beginner).
2. EACH strategy family (Trend, MeanReversion, Breakout, RegimeAware, Benchmark) SHALL have at least one template with at least two presets (e.g. "Conservative" and "Aggressive") where parameter variation is meaningful.
3. THE builder SHALL support four starting points: (a) template strategy, (b) existing strategy version (fork/clone), (c) blank advanced strategy, (d) import from JSON config file.
4. WHEN forking an existing strategy version, THEN the builder SHALL pre-populate all fields from the source version and create a new `StrategyVersion` under the same `StrategyIdentity` with an incremented version number. The `SourceVersionId` field on the new version SHALL reference the forked version for traceability.
5. WHEN importing a JSON config, THEN the builder SHALL parse the file, validate it against the current schema, and populate the builder steps with extracted values. IF the JSON references a removed or unknown strategy type, THEN the builder SHALL show a validation error listing known strategy types.

#### Identity and Versioning Rules

6. WHEN creating from a template, THEN the builder SHALL create a new `StrategyIdentity` with a user-provided name. The `StrategyVersion` SHALL record `SourceType = Template` and `SourceTemplateId` referencing the originating template.
7. WHEN importing a JSON config, THEN the builder SHALL create a new `StrategyIdentity` (not reuse any identity from the imported file). The `StrategyVersion` SHALL record `SourceType = Import` and `ImportedFrom` (original filename or identifier).
8. WHEN forking an existing version, THEN the new `StrategyVersion` SHALL be created under the same `StrategyIdentity` with an incremented version number. The `SourceType` SHALL be `Fork` and `SourceVersionId` SHALL reference the forked version.
9. WHEN creating from blank, THEN the builder SHALL create a new `StrategyIdentity` with `SourceType = Manual`.

---

### Requirement 3 — Preflight Validation

**User Story:** As a researcher, I want the system to validate my strategy configuration before execution, catching errors, warnings, and recommendations in a structured format, so that I avoid wasted runs and understand configuration quality.

#### Acceptance Criteria

1. THE Application layer SHALL define a `PreflightValidator` service with method `Validate(ScenarioConfig config)` returning `PreflightResult`.
2. THE `PreflightResult` record SHALL contain `IReadOnlyList<PreflightFinding>` where each finding has: `Field` (string), `Message` (string), `Severity` (enum: Error/Warning/Recommendation), `Code` (string, e.g. "MISSING_PARAM", "RANGE_VIOLATION").
3. THE validator SHALL check: (a) missing required parameters, (b) parameter values outside min/max range, (c) incompatible timeframe/data selections, (d) unrealistic risk settings (e.g. max exposure > 100%), (e) invalid execution window choices, (f) sealed test set conflicts with research workflow date ranges, (g) insufficient data length for the configured strategy warmup period, (h) BarsPerYear mismatch with declared timeframe.
4. WHEN severity is Error, THEN the run SHALL be blocked. WHEN severity is Warning or Recommendation, THEN the run SHALL proceed but findings SHALL be displayed.
5. THE preflight validator SHALL be invoked by `RunScenarioUseCase` before engine construction. Existing validation in `RunScenarioUseCase` SHALL be migrated into the `PreflightValidator`.
6. THE API SHALL return preflight findings as a structured 400 response when errors are present.
7. THE builder SHALL invoke preflight validation both inline (per-step) and as a final review before launch.

---

### Requirement 4 — Strategy Comparison Improvements

**User Story:** As a researcher, I want to compare two strategy versions side-by-side showing parameter changes, execution assumption differences, and performance deltas with significance indicators, so that I can make informed iteration decisions.

#### Acceptance Criteria

1. THE Application layer SHALL define a `StrategyDiffService` with method `Compare(StrategyVersion a, StrategyVersion b)` returning `StrategyDiff`.
2. THE `StrategyDiff` record SHALL contain: `ParameterChanges` (list of field/old/new), `ExecutionChanges` (list of field/old/new), `DataWindowChanges` (list of field/old/new), `RiskChanges` (list of field/old/new), `RealismChanges` (list of field/old/new), `StageChange` (old/new, nullable), `HypothesisChange` (old/new, nullable).
3. EACH change entry SHALL include a `Significance` flag (enum: Cosmetic/Minor/Material) based on whether the change affects simulation output.
4. THE diff SHALL compare resolved/effective values, not raw stored values, so that preset-derived defaults are visible.
5. THE Web UI SHALL render the diff as a two-column comparison with material changes highlighted.

---

### Requirement 5 — Multi-Step Strategy Builder UX Architecture

**User Story:** As a researcher, I want a multi-step guided builder with progressive disclosure, preset-first configuration, and a live research summary rail, so that strategy creation feels like a guided research workflow rather than a raw configuration form.

#### Acceptance Criteria

1. THE builder SHALL use a 5-step flow: (Step 1) Choose Starting Point, (Step 2) Select Data and Execution Window, (Step 3) Configure Strategy Parameters, (Step 4) Select Realism and Risk Profile, (Step 5) Review and Launch.
2. THE builder SHALL provide a primary content area for the current step and a Research Summary Rail that updates live as the user configures. The exact layout (side-by-side panes, collapsible panel, bottom sheet on narrow viewports) is a design decision, not a hard requirement.
3. THE Research Summary Rail SHALL display: strategy name, strategy family, template/source, data file/symbol, timeframe, execution window, IS/OOS/sealed test split, key parameters (top 3 by sensitivity), risk preset, realism preset, estimated bar count, validation state (error count/warning count), readiness status ("Ready for Quick Run" / "Needs Fixes" / "Research-Grade Ready"), and next recommended action.
4. EACH step SHALL validate before allowing progression to the next step. Back navigation SHALL preserve all entered values.
5. THE builder SHALL persist a `ConfigDraft` on every step transition via `IRepository<ConfigDraft>`, so that in-progress work survives navigation away or application restart. The storage mechanism (JSON file, browser storage, or other) is an Infrastructure concern and SHALL NOT be hard-coded in Application or Web layers.
6. THE builder SHALL be fully functional at 1024px+ viewport width. Responsive behavior for narrower viewports is a design concern addressed in the design document.

**Design Guidance (not acceptance criteria):**
- Preferred layout is a two-pane side-by-side at desktop widths, with the summary rail collapsing to a bottom sheet or toggle panel on narrow viewports.
- Keyboard navigation expectations (Tab/Shift-Tab, Enter to advance, Escape to go back, arrow keys in groups) are specified in Requirement 12.
- Exact color choices for sensitivity badges, validation states, and readiness indicators are design decisions, not requirements. The requirement is that these states are visually distinguishable and carry accessible labels.

**Rationale — Realism and Risk Combined in Step 4:** Realism settings (slippage, commission, fill mode) and risk settings (position sizing, exposure limits) are intentionally combined in a single builder step because they share a common preset model (e.g. "Fast Idea Check" configures both realism and risk together). Separating them would force the user to apply presets across two steps and break the preset-first UX. If future complexity warrants splitting, this is a V5.1 design change.

---

### Requirement 6 — Builder Step 1: Choose Starting Point

**User Story:** As a researcher, I want to choose a starting point for my strategy from templates organized by family, existing versions, blank config, or JSON import, so that I can begin from the most appropriate foundation.

#### Acceptance Criteria

1. Step 1 SHALL present four starting-point options: Template, Existing Version, Blank Advanced, Import Config.
2. WHEN Template is selected, THEN the UI SHALL display strategy family cards. EACH family card SHALL show: family name, plain-language description, common use case, typical markets/timeframes, typical failure mode, and a difficulty badge (Beginner/Intermediate/Advanced).
3. WHEN a family is selected, THEN the UI SHALL show available templates within that family with: display name, hypothesis, and preset options (if available).
4. WHEN Existing Version is selected, THEN the UI SHALL show a searchable list of existing strategies with their latest version, allowing selection and fork.
5. WHEN Import Config is selected, THEN the UI SHALL accept a JSON file upload and parse it into builder state.
6. Step 1 SHALL require the user to enter a `Hypothesis` (free text, required, minimum 10 characters) describing the expected market edge, and an `ExpectedFailureMode` (free text, optional) describing how the strategy is most likely to fail. These fields SHALL be stored on the `StrategyVersion` and displayed in the Research Summary Rail and Strategy Workspace Overview.

---

### Requirement 7 — Builder Step 2: Data and Execution Window

**User Story:** As a researcher, I want to select my data source, timeframe, date range, and IS/OOS/sealed split with live diagnostics, so that I configure the execution window correctly before tuning parameters.

#### Acceptance Criteria

1. Step 2 SHALL allow selection of: data file (from validated files), timeframe, date range (start/end), IS/OOS split percentage, and optional sealed test set percentage.
2. THE step SHALL display a timeline visualization showing: full data range, training (IS) window, OOS window, and sealed test set (if configured), with drag handles for adjustment.
3. THE step SHALL show live diagnostics: estimated bar count, insufficient data warning (below strategy warmup + 100 bars minimum), timeframe mismatch warning (if data file timeframe differs from selected), overlap or leakage warnings (if sealed set overlaps IS/OOS), data validation issues from the `DataFileRecord`, and underpowered study warning (if bar count is below `MinBtlCalculator` threshold for the strategy's expected Sharpe).
4. WHEN the user changes timeframe, THEN `BarsPerYear` SHALL auto-update to the canonical value (Daily=252, H4=1512, H1=6048, M15=24192).
5. THE data file picker SHALL show only files with `ValidationStatus.Valid` by default, with a toggle to show all files.

---

### Requirement 8 — Builder Step 3: Strategy Parameters

**User Story:** As a researcher, I want to configure strategy parameters in grouped sections with simple/advanced modes, inline help, sensitivity hints, and validation, so that I understand what each parameter does and how it affects overfitting risk.

#### Acceptance Criteria

1. Step 3 SHALL render parameters grouped by `StrategyParameterSchema.Group` (Signal, Entry, Exit, Risk, Filters, Execution) with collapsible group headers.
2. THE step SHALL support two modes: Simple (showing only parameters where `IsAdvanced == false`) and Advanced (showing all parameters). Simple mode SHALL be the default.
3. EACH parameter row SHALL display: label (`DisplayName`), type-specific input control, current value, default value indicator, help text (`Description`), sensitivity hint badge (Low=green, Medium=amber, High=red with "overfitting risk" tooltip), and inline validation (red border + message on invalid).
4. EACH parameter row SHALL include a "Reset to Default" action that restores the parameter to its `DefaultValue`.
5. WHEN a template preset is active (e.g. "Conservative"), THEN parameters overridden by the preset SHALL show a "Preset" badge. Manually editing a preset parameter SHALL clear the preset badge for that parameter.
6. EACH parameter row SHALL display a provenance indicator showing whether the current value comes from: default, preset, explicit user edit, or override. This provenance SHALL be visible during editing, not only in the final resolved config review (Requirement 16).

---

### Requirement 9 — Builder Step 4: Realism and Risk Profile

**User Story:** As a researcher, I want to select realism and risk settings starting from named presets with progressive disclosure into detailed controls, so that I configure execution assumptions appropriate to my research stage.

#### Acceptance Criteria

1. Step 4 SHALL present four realism presets: "Fast Idea Check" (FastResearch profile, zero slippage, zero commission), "Standard Backtest" (StandardBacktest profile, fixed spread slippage, per-trade commission), "Conservative Realistic" (BrokerConservative profile, ATR-scaled slippage, per-share commission, session rules), "Research-Grade Validation" (BrokerConservative profile + sensitivity analysis recommendation).
2. EACH preset card SHALL include a "What This Changes" expandable panel listing the specific engine settings the preset configures.
3. BELOW the presets, an "Advanced Overrides" expandable section SHALL allow fine-grained control of: fill mode, slippage model and parameters, commission model and parameters, spread assumptions, session calendar, position sizing policy, exposure limits, and prop-firm constraints.
4. WHEN an advanced override conflicts with the selected preset, THEN the preset label SHALL change to "Custom (based on [preset name])".
5. THE step SHALL validate that risk settings are internally consistent (e.g. max exposure percent > 0, initial cash > 0).
6. EACH realism and risk field SHALL display a provenance indicator (default / preset / explicit override) during editing, consistent with the parameter provenance in Step 3 (Requirement 8, AC 6).

---

### Requirement 10 — Builder Step 5: Review and Launch

**User Story:** As a researcher, I want a preflight review showing my complete effective configuration, validation findings, and launch options, so that I can confirm everything before committing to a run.

#### Acceptance Criteria

1. Step 5 SHALL display the full resolved configuration organized by section: Strategy Identity (name, family, hypothesis), Data Selection (file, timeframe, date range, bar count), Execution Window (IS/OOS/sealed split), Key Parameters (all non-default values highlighted), Realism and Risk Assumptions (preset name + overrides), and Estimated Runtime.
2. Step 5 SHALL display all preflight validation findings grouped by severity (Errors → Warnings → Recommendations).
3. WHEN errors exist, THEN the "Run" actions SHALL be disabled with a message "Fix N errors before running".
4. Step 5 SHALL offer three launch actions: "Run Quick Sanity Test" (single run with FastResearch preset override), "Run Standard Backtest" (single run with configured settings), and "Save Draft Without Running" (persists `ConfigDraft` and `StrategyVersion` without execution).
5. AFTER a successful save or run launch, the builder SHALL navigate to the Strategy Workspace for the created/updated strategy.

---

### Requirement 11 — Post-Create Strategy Workspace Handoff

**User Story:** As a researcher, I want the builder to hand off into a Strategy Workspace with overview, runs, research, and versions tabs, so that I can immediately continue the research workflow after creating a strategy.

#### Acceptance Criteria

1. THE Strategy Workspace SHALL be the existing Strategy Detail screen enhanced with a default Overview tab showing: latest run summary (or "No runs yet" empty state), current development stage, research checklist progress, key warnings from the latest run, recommended next study (based on `StrategyDescriptor.SuggestedStudies` and checklist gaps), and shortcut actions (Compare Versions, Edit Assumptions, Launch Study).
2. WHEN the builder completes with "Run Quick Sanity Test" or "Run Standard Backtest", THEN the workspace SHALL open with the Runs tab active showing the in-progress run.
3. WHEN the builder completes with "Save Draft Without Running", THEN the workspace SHALL open with the Overview tab showing a prompt to run the first backtest.

---

### Requirement 12 — Builder UI States and Accessibility

**User Story:** As a researcher, I want the builder to handle all edge cases (empty states, loading, validation errors, dirty forms, cancellation) gracefully with keyboard navigation and accessibility support, so that the experience is robust and inclusive.

#### Acceptance Criteria

1. THE builder SHALL define and handle these UI states: empty (no templates loaded — show retry), loading (data files scanning — show skeleton), validation error (inline per-field + summary), failed run (error banner with retry), cancelled job (status banner), partial result (warning banner), save-draft (success toast), dirty form (unsaved changes prompt on navigation away).
2. ALL builder steps SHALL be fully navigable via keyboard. Users SHALL be able to move between fields, advance/retreat steps, and interact with all controls without a mouse.
3. ALL form fields SHALL have associated `<label>` elements. ALL icon-only buttons SHALL carry `aria-label`. ALL status badges SHALL have `aria-label` in addition to visual indicators.
4. THE builder SHALL be fully functional at 1024px+ viewport width. Behavior at narrower viewports is a design concern.

**Design Guidance (not acceptance criteria):**
- Recommended keyboard bindings: Tab/Shift-Tab between fields, Enter to advance step (when valid), Escape to go back, arrow keys within radio/select groups. Exact bindings may be adjusted during implementation.
- Exact mobile/tablet collapse behavior (e.g. summary rail as bottom sheet at 768px) is a design decision.
- Exact color choices for validation states, sensitivity badges, and status indicators are design decisions. The requirement is visual distinguishability plus accessible labels.


---

## AREA B — Scenario / Configuration Ergonomics

### Requirement 13 — ScenarioConfig Sub-Object Decomposition

**User Story:** As a developer, I want `ScenarioConfig` decomposed into focused sub-objects (DataConfig, StrategyConfig, RiskConfig, ExecutionConfig, ResearchConfig) with a backward-compatible adapter, so that configuration is easier to understand, validate, and extend without breaking existing JSON payloads.

#### Acceptance Criteria

1. THE Core layer SHALL define five new record types: `DataConfig` (DataProviderType, DataProviderOptions, Timeframe, BarsPerYear), `StrategyConfig` (StrategyType, StrategyParameters), `RiskConfig` (RiskParameters, InitialCash, AnnualRiskFreeRate), `ExecutionConfig` (SlippageModelType, CommissionModelType, FillMode, RealismProfile, ExecutionOptions, SessionOptions), `ResearchConfig` (ResearchWorkflowType, ResearchWorkflowOptions, RandomSeed, TraceOptions).
2. THE `ScenarioConfig` record SHALL gain optional sub-object properties (`DataConfig?`, `StrategyConfig?`, `RiskConfig?`, `ExecutionConfig?`, `ResearchConfig?`) as trailing parameters with default null, preserving all existing top-level properties.
3. THE `ScenarioConfig` SHALL expose computed `Effective*` properties (e.g. `EffectiveDataConfig`) that return the sub-object if present, or construct one from the top-level properties as fallback. This is the single source of truth for engine consumption.
4. WHEN both a sub-object and the corresponding top-level properties are present in a JSON payload, THEN the sub-object SHALL take precedence and the `PreflightValidator` SHALL emit a Warning finding: "Both top-level and sub-object values present for [section]; sub-object values will be used."
5. ALL existing JSON payloads with only top-level properties SHALL continue to deserialize and execute without modification.
6. NEW JSON payloads generated by the builder and API SHALL use the sub-object format exclusively.

---

### Requirement 14 — Explicit Override Rules and Precedence Validation

**User Story:** As a researcher, I want configuration precedence rules to be explicit and validated, with warnings when legacy payloads rely on deprecated patterns, so that I always know which values the engine will actually use.

#### Acceptance Criteria

1. THE `PreflightValidator` SHALL detect and warn on all precedence conflicts: (a) `ExecutionOptions.FillModeOverride` vs top-level `FillMode`, (b) sub-object values vs top-level values for the same field, (c) preset-derived values vs explicit overrides.
2. EACH precedence warning SHALL state which value wins and why (e.g. "ExecutionOptions.FillModeOverride (IntraBar) takes precedence over top-level FillMode (NextBarOpen)").
3. THE resolved config view (Requirement 16) SHALL show the winning value for every field with a provenance indicator (default / preset / explicit / override).

---

### Requirement 15 — Configuration Presets

**User Story:** As a researcher, I want reusable configuration presets for common research scenarios, so that I can quickly configure runs appropriate to my current research stage without manually setting dozens of fields.

#### Acceptance Criteria

1. THE Application layer SHALL define a `ConfigPreset` record with fields: `PresetId` (string), `Name` (string), `Description` (string), `Category` (enum: QuickCheck/Standard/Realistic/ResearchGrade), `ExecutionConfig` (ExecutionConfig), `RiskConfig` (partial RiskConfig overrides, nullable), `IsBuiltIn` (bool).
2. THE system SHALL ship with four built-in presets: "Fast Idea Check" (FastResearch, zero costs, relaxed risk), "Standard Backtest" (StandardBacktest, moderate costs, standard risk), "Conservative Realistic" (BrokerConservative, ATR-scaled slippage, per-share commission, session rules), "Research-Grade Validation" (BrokerConservative + recommendation to run sensitivity and walk-forward studies).
3. WHEN a preset is applied, THEN the preset values SHALL populate the corresponding config fields. The user SHALL be able to override any preset value.
4. THE API SHALL accept an optional `presetId` field on run requests. WHEN present, the preset SHALL be applied before any explicit field values, and explicit values SHALL override preset values.
5. Custom presets SHALL be persistable via `IRepository<ConfigPreset>` in Infrastructure.

---

### Requirement 16 — Resolved Configuration Explainability

**User Story:** As a researcher, I want to see the final effective configuration the engine will use after all defaults, presets, and overrides are applied, so that I can verify exactly what will run.

#### Acceptance Criteria

1. THE Application layer SHALL define a `ResolvedConfigService` with method `Resolve(ScenarioConfig config, string? presetId)` returning `ResolvedConfig`.
2. THE `ResolvedConfig` record SHALL contain all effective values organized by section (Data, Strategy, Risk, Execution, Research) with each value annotated by provenance: `Default`, `Preset`, `Explicit`, `Override`.
3. THE API SHALL expose a `POST /scenarios/resolve` endpoint that accepts a `ScenarioConfig` (or sub-object format) and returns the `ResolvedConfig` without executing a run.
4. THE builder Step 5 (Review) SHALL display the resolved config with provenance indicators.
5. THE `BacktestResult.ExperimentMetadata` SHALL include a reproducibility snapshot: resolved config used for the run, data file identity (filename, hash or last-modified timestamp), random seed, engine version string, and preset ID (if applicable), enabling exact reproduction.

---

## AREA C — API and Job Workflow Improvements

### Requirement 17 — Job-Based Async Execution

**User Story:** As an API consumer, I want long-running backtests and studies to execute as async jobs with lifecycle management, so that I can submit work, poll for progress, and retrieve results without blocking HTTP connections.

#### Acceptance Criteria

1. THE Application layer SHALL define a `BacktestJob` record with fields: `JobId` (string), `JobType` (enum: SingleRun/MonteCarlo/WalkForward/ParameterSweep/Sensitivity/Stability/Realism/Perturbation/RegimeSegmentation/BenchmarkComparison), `Status` (enum: Queued/Running/Completed/Failed/Cancelled), `SubmittedAt` (DateTimeOffset), `StartedAt` (nullable), `CompletedAt` (nullable), `Progress` (nullable ProgressSnapshot), `ResultId` (nullable string), `ErrorMessage` (nullable string).
2. THE API SHALL expose: `POST /jobs` (submit a job, returns 202 with `JobId` and `Location` header), `GET /jobs/{jobId}` (returns current job state including progress), `DELETE /jobs/{jobId}` (cancels a running job, returns 200), `GET /jobs/{jobId}/result` (returns the result when completed, 404 when not ready).
3. EXISTING synchronous endpoints (`POST /scenarios/run`, `/sweep`, `/montecarlo`, `/walkforward`) SHALL remain functional for backward compatibility. They SHALL internally create a job and wait for completion before returning the result.
4. THE job executor SHALL use `CancellationToken` propagation so that `DELETE /jobs/{jobId}` triggers cooperative cancellation.
5. WHEN a job fails, THEN the job record SHALL store the error message (user-friendly, no stack traces) and the API SHALL return it in the `GET /jobs/{jobId}` response.
6. Job records SHALL be persisted via `IRepository<BacktestJob>` so they survive application restart.
7. THE `BacktestJob` record SHALL include a `ReproducibilitySnapshot` field containing: resolved config snapshot (as per Requirement 16), data file identity (filename, hash or last-modified timestamp), random seed, engine version string, and preset ID (if applicable). This snapshot SHALL be sufficient to reproduce the exact run.

---

### Requirement 18 — Standardized Progress Reporting

**User Story:** As a researcher, I want consistent progress reporting across all execution types (single runs, Monte Carlo, walk-forward, parameter sweep, sensitivity, stability studies), so that I can monitor long-running work and estimate completion time.

#### Acceptance Criteria

1. THE `IProgressReporter` interface (already defined) SHALL be extended with a `Report(ProgressSnapshot snapshot)` method where `ProgressSnapshot` is a record with: `Current` (int), `Total` (int), `Percentage` (decimal), `Stage` (string, e.g. "Simulating", "Optimizing", "Evaluating"), `CurrentItemLabel` (string, nullable), `ElapsedTime` (TimeSpan), `Warnings` (IReadOnlyList<string>).
2. ALL research workflows (MonteCarloWorkflow, WalkForwardWorkflow, ParameterSweepWorkflow, SensitivityAnalysisWorkflow, ParameterStabilityWorkflow, RealismSensitivityWorkflow, ParameterPerturbationWorkflow, RegimeSegmentationWorkflow, BenchmarkComparisonWorkflow) SHALL report progress via `IProgressReporter` at each iteration/window/path completion.
3. `RunScenarioUseCase` SHALL report progress at configurable bar intervals (default: every 10% of total bars) for single runs.
4. THE `GET /jobs/{jobId}` endpoint SHALL include the latest `ProgressSnapshot` in the response body.
5. THE Web UI execution status bar SHALL consume progress snapshots to display: percentage, stage label, current/total count, and elapsed time.

---

### Requirement 19 — Endpoint Ergonomics and Request DTOs

**User Story:** As an API consumer, I want explicit, well-typed request DTOs for all endpoints with consistent validation and error responses, so that I can integrate reliably without guessing which fields are honored.

#### Acceptance Criteria

1. EACH API endpoint SHALL have a dedicated request DTO record (e.g. `RunScenarioRequest`, `SweepRequest`, `MonteCarloRequest`, `WalkForwardRequest`, `SubmitJobRequest`) defined in the Api project.
2. ALL request DTOs SHALL use the V5 sub-object config format (`DataConfig`, `StrategyConfig`, `RiskConfig`, `ExecutionConfig`, `ResearchConfig`) as the primary shape, with a flat `ScenarioConfig` fallback for backward compatibility.
3. EACH request DTO SHALL be validated via the `PreflightValidator` before execution. Validation errors SHALL return HTTP 400 with the standard `{ "errors": [...] }` shape, extended to include `severity` and `code` fields per finding.
4. THE sweep, Monte Carlo, and walk-forward endpoints SHALL fully honor all caller-supplied options (simulation count, window size, seed, etc.) without silently falling back to defaults. WHEN a required option is missing, THEN the endpoint SHALL return a 400 error, not a silent default.
5. ALL endpoints SHALL have `.WithName()`, `.WithTags()`, and `.Produces<T>()` OpenAPI annotations.

---

### Requirement 20 — Strategy and Schema Discovery Endpoints

**User Story:** As an API consumer or UI client, I want endpoints to discover available strategies, parameter schemas, research workflows, config presets, and supported execution models, so that I can build dynamic UIs and integrations without hard-coding knowledge.

#### Acceptance Criteria

1. THE API SHALL expose `GET /strategies` returning a list of `{ name, displayName, family, description, hypothesis, bestFor, suggestedStudies, difficultyLevel }` for all registered strategies.
2. THE API SHALL expose `GET /strategies/{name}/schema` returning the typed `StrategyParameterSchema` list for the named strategy.
3. THE API SHALL expose `GET /workflows` returning a list of available research workflow types with: name, description, required parameters, and typical use case.
4. THE API SHALL expose `GET /presets` returning all available config presets (built-in and custom).
5. THE API SHALL expose `GET /execution-models` returning lists of: supported slippage models, commission models, fill modes, realism profiles, session calendars, and position sizing policies — each with name and description.
6. ALL discovery endpoints SHALL be GET requests, cacheable, and tagged with "Discovery" in OpenAPI.
7. THE `GET /strategies/{name}/schema` response SHALL include a `SchemaVersion` (string, e.g. "1.0") field. WHEN a schema changes in a backward-incompatible way, the version SHALL be incremented.
8. ALL discovery endpoint responses SHALL include optional `DeprecatedFields` (list of field names with deprecation reason and suggested replacement) and `CompatibilityNotes` (free text, nullable) fields, so that API consumers can detect and adapt to schema evolution programmatically.

---

## AREA D — User Journey and Product UX

### Requirement 21 — Guided Research Workflow Recommendations

**User Story:** As a researcher, I want the system to recommend next research steps based on my strategy's current development stage and completed studies, so that I follow a disciplined validation process without needing to memorize the research methodology.

#### Acceptance Criteria

1. THE `ResearchChecklistService` (already exists) SHALL be extended to compute a `NextRecommendedAction` based on: current `DevelopmentStage`, completed study types, latest run metrics, and `StrategyDescriptor.SuggestedStudies`.
2. THE recommendation SHALL distinguish between "quick check" mode (suggest Monte Carlo after first run) and "research-grade" mode (suggest full walk-forward → sensitivity → stability → sealed test sequence).
3. WHEN the user appears to be over-optimizing (more than 5 parameter sweep runs without a walk-forward study), THEN the system SHALL surface a warning: "Consider running a walk-forward study to check for overfitting before further parameter tuning."
4. THE Strategy Workspace Overview tab SHALL display the next recommended action as a prominent card with a one-click launch button.
5. THE Research Summary Rail in the builder SHALL show the recommended research path for the selected strategy family.

---

### Requirement 22 — Result Interpretation Improvements

**User Story:** As a researcher, I want backtest results to highlight the most decision-relevant metrics, show realism assumptions prominently, compare IS vs OOS performance clearly, and indicate whether a strategy passes a minimum research checklist, so that I can make informed decisions without deep quant expertise.

#### Acceptance Criteria

1. THE Run Detail screen SHALL organize metrics into three tiers: (Tier 1, above the fold) Sharpe, Max Drawdown, Win Rate, K-Ratio, DSR; (Tier 2, visible in tabs) Sortino, Calmar, Profit Factor, Expectancy, Recovery Factor, Average Holding Period; (Tier 3, behind expanders) regime breakdown, MAE/MFE, event trace.
2. THE Run Detail screen SHALL display a "Realism Assumptions" card showing: realism profile name, fill mode, slippage model, commission model, and session calendar — always visible, not hidden in config details.
3. WHEN both IS and OOS results are available (from walk-forward or split runs), THEN the Run Detail screen SHALL show an IS vs OOS comparison panel with: Sharpe ratio comparison, drawdown comparison, and a degradation percentage.
4. THE Run Detail screen SHALL display a "Research Readiness" badge: "Minimum Checklist Passed" (green) when at least initial backtest + Monte Carlo + walk-forward are completed, "Incomplete" (amber) otherwise, with a link to the research checklist.
5. WHEN a metric triggers a robustness warning (Sharpe > 3, trades < 30, K-Ratio < 0, fragility > 0.7, cost sensitivity > 50% Sharpe degradation), THEN the warning badge SHALL include a one-sentence explanation of why this is concerning.


---

## AREA E — Quant / Research Improvements

### Requirement 23 — Execution Realism Enhancements

**User Story:** As a quant researcher, I want improved execution realism covering gap handling, limit/stop execution fidelity, volume/liquidity constraints, and bar-resolution caveats, so that backtest results more closely approximate real trading conditions.

#### Acceptance Criteria

1. THE engine SHALL detect overnight/weekend gaps (price jump > 2× ATR between consecutive bars) and apply gap-adjusted fill prices for stop and limit orders that would have been triggered during the gap. Gap fills SHALL use the opening price of the gap bar, not the stop/limit price.
2. THE `ExecutionOptions` record SHALL gain a `MaxFillPercentOfVolume` (decimal, nullable, default null) field. WHEN set, THEN the `SimulatedExecutionHandler` SHALL cap fill quantity at the specified percentage of the bar's volume, with the remainder becoming a partial fill or rejection.
3. THE `SimulatedExecutionHandler` SHALL log a warning when a fill quantity exceeds 10% of bar volume (even when `MaxFillPercentOfVolume` is not set), as a realism advisory.
4. THE `BacktestResult` SHALL gain a `RealismAdvisories` field (IReadOnlyList<string>, nullable) collecting all realism warnings generated during the run (gap fills, volume warnings, session boundary fills).
5. ALL new realism features SHALL be opt-in via `ExecutionOptions` fields with null/disabled defaults, preserving existing behavior for current configurations.

---

### Requirement 24 — Overfitting Defense Systematization

**User Story:** As a quant researcher, I want systematized overfitting defenses including parameter stability scoring, trial budget tracking, minimum sample size enforcement, and deflated Sharpe warnings integrated into the research workflow, so that I can identify curve-fitted strategies before risking capital.

#### Acceptance Criteria

1. THE `PreflightValidator` SHALL enforce a minimum bar count check: WHEN actual bar count is below `MinBtlCalculator.Compute(observedSharpe, trialCount, skewness, kurtosis)` at 95% confidence, THEN a Warning finding SHALL be emitted: "This backtest may be too short to be statistically significant (N bars available, M bars recommended)."
2. THE `StrategyVersion.TotalTrialsRun` counter SHALL be incremented by `RunScenarioUseCase` after every completed run and by `ParameterSweepWorkflow` by the number of combinations evaluated.
3. WHEN `DeflatedSharpeRatio` (already computed) is below 0.95, THEN the Run Detail screen SHALL display a "Possible Overfitting" warning badge with explanation: "After adjusting for N trials, the observed Sharpe may not be statistically significant."
4. THE `ParameterStabilityWorkflow` result SHALL include a `FragilityScore` (0.0–1.0) and a `StableRegionPercent` (percentage of parameter space where Sharpe remains within 20% of optimal). WHEN `FragilityScore > 0.7`, THEN a "Parameter Island" warning SHALL be surfaced.
5. THE Research Checklist SHALL include a "Trial Budget" item that turns amber when `TotalTrialsRun > 20` without a walk-forward study, and red when `TotalTrialsRun > 50` without a walk-forward study.

---

### Requirement 25 — Portfolio and Cross-Asset Foundation

**User Story:** As a quant researcher, I want foundational support for multi-asset portfolio awareness including per-symbol exposure limits, portfolio-level position sizing, and correlation-aware constraints, so that the engine can evolve toward realistic multi-asset research.

#### Acceptance Criteria

1. THE `PortfolioConstraints` record (already exists in Application/Risk) SHALL be extended with: `MaxExposurePerSymbol` (decimal, nullable), `MaxExposurePerSector` (decimal, nullable), `MaxCorrelatedExposure` (decimal, nullable — V5.1 roadmap, field defined but not enforced).
2. THE `Portfolio` class SHALL track positions by symbol (already does) and expose `GetExposureBySymbol()` returning a dictionary of symbol → exposure percentage.
3. THE `DefaultRiskLayer` SHALL enforce `MaxExposurePerSymbol` when set: WHEN a new order would cause a single symbol's exposure to exceed the limit, THEN the order SHALL be rejected with a `RiskRejection` log event.
4. THE `ScenarioConfig` (via `RiskConfig` sub-object) SHALL accept `PortfolioConstraints` as a configurable field.
5. Correlation-aware constraints (`MaxCorrelatedExposure`) and sector-level limits SHALL be defined as fields but enforcement SHALL be deferred to V5.1 with a clear "not yet enforced" note in XML doc comments.

---

### Requirement 26 — Short-Selling Extensibility Path

**User Story:** As a developer, I want the engine prepared for long/short support with isolated long-only assumptions, adapter patterns, and protective tests, so that short-selling can be added incrementally without a breaking rewrite.

#### Acceptance Criteria

1. THE `Direction` enum in Core SHALL gain a `Short` value. ALL existing code that switches on `Direction` SHALL handle the `Short` case by throwing `NotSupportedException("Short selling is not yet supported")` — this is a compile-time safety net, not a runtime feature.
2. THE `Portfolio` class SHALL isolate all long-only assumptions into clearly marked methods (e.g. `ValidateLongOnly(Direction direction)` called at entry points). These methods SHALL throw `NotSupportedException` for `Direction.Short`.
3. A `LongOnlyGuard` static helper in Core SHALL provide `EnsureLongOnly(Direction direction)` for use across the codebase. ALL current `Direction` consumers SHALL call this guard.
4. A new test class `ShortSellingGuardTests` in UnitTests SHALL verify that: (a) `Direction.Short` exists in the enum, (b) `LongOnlyGuard.EnsureLongOnly(Direction.Short)` throws `NotSupportedException`, (c) `Portfolio` rejects short orders with `NotSupportedException`, (d) all existing strategies only emit `Long` or `Flat`.
5. THE `IStrategy` contract SHALL NOT change. Strategies that want to go short in the future will emit `Direction.Short`; the guard removal is a V6 task.

---

### Requirement 27 — Benchmarking and Evaluation Improvements

**User Story:** As a quant researcher, I want improved benchmarking with automatic buy-and-hold comparison, regime-aware evaluation summaries, and per-market-condition performance breakdowns, so that I can evaluate strategy edge more rigorously.

#### Acceptance Criteria

1. THE `BenchmarkComparisonWorkflow` (already exists) SHALL be extended to automatically include a buy-and-hold run using `BaselineBuyAndHoldStrategy` with the same data and execution window when no explicit benchmark is specified.
2. THE benchmark comparison result SHALL include: excess return (strategy Sharpe minus benchmark Sharpe), information ratio, tracking error, and maximum relative drawdown.
3. THE `RegimeSegmentationWorkflow` result SHALL include per-regime performance summaries: for each detected regime (e.g. high-vol, low-vol, trending, mean-reverting), show Sharpe, max drawdown, trade count, and win rate.
4. THE Run Detail screen SHALL display a "Performance by Regime" expandable section when regime segmentation data is available.
5. THE Strategy Workspace Overview SHALL show a "vs Buy & Hold" comparison chip when a benchmark comparison study exists, displaying excess Sharpe and a directional arrow.

---

## AREA F — Codebase Shape / Maintainability

### Requirement 28 — Naming and Structure Cleanup

**User Story:** As a developer, I want consistent folder naming and clear separation between strategy metadata, registry, templates, and implementations, so that the codebase is navigable and conventions are obvious.

#### Acceptance Criteria

1. THE Application layer SHALL consolidate strategy-related code into two folders: `Strategy/` (metadata, registry, templates, descriptors, identity, versioning, schema provider, diff service) and `Strategies/` (IStrategy implementations only). This matches the current layout and SHALL be documented as the canonical convention.
2. THE `StrategyParameterSchema`, `ParameterMetaAttribute`, `IStrategySchemaProvider`, `StrategyDiffService`, `PreflightValidator`, `ResolvedConfigService`, and `ConfigPreset` SHALL live in `Strategy/`.
3. THE `ConfigDraft` record SHALL live in `Strategy/` (it is a builder concept, not an engine concept).
4. ALL new V5 types SHALL include XML doc comments on all public members.
5. A `docs/V5-Developer-Guide.md` document SHALL be created covering: how to add a new strategy with schema metadata, how to add a config preset, how to extend the preflight validator, and how to add a discovery endpoint.

---

### Requirement 29 — Backward Compatibility and Migration

**User Story:** As an existing user, I want all existing JSON payloads (ScenarioConfig, BacktestResult, StrategyIdentity, StrategyVersion) to continue working after V5 changes, with clear migration paths for deprecated patterns.

#### Acceptance Criteria

1. ALL new fields on existing records SHALL be added as trailing parameters with default values, preserving backward-compatible JSON deserialization.
2. THE `ScenarioConfig` sub-object decomposition SHALL NOT break existing flat JSON payloads. The `Effective*` computed properties SHALL construct sub-objects from top-level fields when sub-objects are absent.
3. WHEN the API receives a V4-format flat `ScenarioConfig`, THEN the system SHALL process it identically to before. A `Deprecation` response header SHALL be included: "X-Deprecation: Flat ScenarioConfig format is deprecated; use sub-object format. See /docs/migration."
4. THE `BacktestResult` record's new fields (`RealismAdvisories`) SHALL deserialize to null/empty from existing JSON files.
5. A `docs/V5-Migration-Guide.md` document SHALL describe: config format changes, new fields on existing records, deprecated patterns, and recommended migration steps.

---

### Requirement 30 — Testing Requirements

**User Story:** As a developer, I want every major V5 feature to specify its test strategy including unit tests, integration tests, property-based tests, and JSON round-trip tests, so that quality is maintained as the codebase grows.

#### Acceptance Criteria

1. THE `PreflightValidator` SHALL have unit tests covering: all error conditions (missing params, range violations, timeframe mismatch, sealed set conflict, insufficient data), warning conditions (precedence conflicts, deprecated patterns), and recommendation conditions (research-grade suggestions).
2. THE `StrategyParameterSchema` provider SHALL have unit tests verifying: schema generation from `[ParameterMeta]` attributes, fallback schema from constructor parameters, and schema for all 6 built-in strategies.
3. THE `ResolvedConfigService` SHALL have unit tests verifying: default resolution, preset application, explicit override precedence, and provenance annotation correctness.
4. THE `StrategyDiffService` SHALL have unit tests verifying: parameter changes detected, execution changes detected, significance classification, and no false positives on identical versions.
5. THE `ScenarioConfig` sub-object decomposition SHALL have a property-based test: FOR ALL valid `ScenarioConfig` instances, constructing via flat properties and then reading via `Effective*` sub-objects SHALL produce values identical to constructing via sub-objects directly.
6. THE `BacktestJob` lifecycle SHALL have integration tests via `WebApplicationFactory`: submit job → poll progress → retrieve result, and submit job → cancel → verify cancelled status.
7. ALL new API endpoints SHALL have integration tests verifying: success responses, 400 validation errors, and OpenAPI annotation presence.
8. THE `Direction.Short` guard SHALL have unit tests as specified in Requirement 26.

---

### Requirement 31 — Documentation Requirements

**User Story:** As a developer, I want updated documentation covering strategy schema metadata, config presets, resolved config, research workflows, and quant assumptions, so that contributors can extend the system confidently.

#### Acceptance Criteria

1. `docs/V5-Developer-Guide.md` SHALL cover: adding a new strategy with `[ParameterMeta]` attributes (step-by-step with code example), adding a config preset, extending the preflight validator with new rules, adding a discovery endpoint, and the sub-object config format.
2. `docs/V5-Migration-Guide.md` SHALL cover: config format changes (flat → sub-object), new fields on existing records, deprecated patterns and timeline, API versioning approach, and JSON payload examples (before/after).
3. `docs/V5-Quant-Assumptions.md` SHALL cover: execution realism model descriptions (each slippage/commission model), gap handling behavior, volume constraint behavior, bar-resolution caveats and known limitations, long-only scope and short-selling roadmap, overfitting defense methodology (DSR, MinBTL, trial budget, fragility scoring), and benchmarking methodology.
4. ALL existing docs that reference `ScenarioConfig` flat format SHALL be updated to show both formats with a note that sub-object format is preferred.

---

## V5.1 / V6 Roadmap (Explicitly Out of Scope for V5)

The following items are acknowledged but deferred:

- **No-code / DSL strategy authoring** — V6 roadmap. V5 provides schema metadata as the foundation.
- **Full CPCV implementation** — V5.1. The `StudyType.CombinatorialPurgedCV` enum entry exists; implementation is deferred.
- **Correlation-aware portfolio constraints** — V5.1. The `MaxCorrelatedExposure` field exists; enforcement is deferred.
- **Sector-level exposure limits** — V5.1. The `MaxExposurePerSector` field exists; enforcement is deferred.
- **Short-selling execution** — V6. V5 adds `Direction.Short` to the enum and guards; actual execution logic is deferred.
- **Multi-currency portfolio tracking** — V6.
- **Database persistence** — V6. JSON file persistence remains the V5 standard.
- **WebSocket/SSE real-time progress streaming** — V5.1. V5 uses polling via `GET /jobs/{jobId}`.
- **Multi-user / authentication** — Out of scope indefinitely for this product.
- **Plugin marketplace / community strategies** — V6 roadmap.

---

## Requirements Index

| ID | Title | Area | Requirement # |
|----|-------|------|---------------|
| REQ-V5-01 | Typed Strategy Parameter Schema | A | 1 |
| REQ-V5-02 | Strategy Template and Builder Support | A | 2 |
| REQ-V5-03 | Preflight Validation | A | 3 |
| REQ-V5-04 | Strategy Comparison Improvements | A | 4 |
| REQ-V5-05 | Multi-Step Strategy Builder UX Architecture | A | 5 |
| REQ-V5-06 | Builder Step 1: Choose Starting Point | A | 6 |
| REQ-V5-07 | Builder Step 2: Data and Execution Window | A | 7 |
| REQ-V5-08 | Builder Step 3: Strategy Parameters | A | 8 |
| REQ-V5-09 | Builder Step 4: Realism and Risk Profile | A | 9 |
| REQ-V5-10 | Builder Step 5: Review and Launch | A | 10 |
| REQ-V5-11 | Post-Create Strategy Workspace Handoff | A | 11 |
| REQ-V5-12 | Builder UI States and Accessibility | A | 12 |
| REQ-V5-13 | ScenarioConfig Sub-Object Decomposition | B | 13 |
| REQ-V5-14 | Explicit Override Rules and Precedence Validation | B | 14 |
| REQ-V5-15 | Configuration Presets | B | 15 |
| REQ-V5-16 | Resolved Configuration Explainability | B | 16 |
| REQ-V5-17 | Job-Based Async Execution | C | 17 |
| REQ-V5-18 | Standardized Progress Reporting | C | 18 |
| REQ-V5-19 | Endpoint Ergonomics and Request DTOs | C | 19 |
| REQ-V5-20 | Strategy and Schema Discovery Endpoints | C | 20 |
| REQ-V5-21 | Guided Research Workflow Recommendations | D | 21 |
| REQ-V5-22 | Result Interpretation Improvements | D | 22 |
| REQ-V5-23 | Execution Realism Enhancements | E | 23 |
| REQ-V5-24 | Overfitting Defense Systematization | E | 24 |
| REQ-V5-25 | Portfolio and Cross-Asset Foundation | E | 25 |
| REQ-V5-26 | Short-Selling Extensibility Path | E | 26 |
| REQ-V5-27 | Benchmarking and Evaluation Improvements | E | 27 |
| REQ-V5-28 | Naming and Structure Cleanup | F | 28 |
| REQ-V5-29 | Backward Compatibility and Migration | F | 29 |
| REQ-V5-30 | Testing Requirements | F | 30 |
| REQ-V5-31 | Documentation Requirements | F | 31 |
