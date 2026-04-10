# Implementation Plan — TradingResearchEngine V4

- [x] 1. Core layer minimal additions



- [x] 1.1 Add `FailureDetail`, `DeflatedSharpeRatio`, `TrialCount` to `BacktestResult`


  - Nullable trailing fields after `StrategyVersionId`, backwards-compatible JSON


  - _Requirements: 2.2, 19.1, 19.5_







- [x] 1.2 Add `DateRangeConstraint` value object to Core/Configuration


  - `readonly record struct`, Start inclusive / End exclusive: [Start, End)


  - _Requirements: 20.8_


- [x] 1.3 Write unit tests for Core additions


  - Round-trip JSON tests, boundary semantics test


  - _Requirements: 2.2, 20.8_





- [ ] 2. Application layer domain model amendments
- [x] 2.1 Add `DevelopmentStage` enum to Application/Strategy

  - Values: Hypothesis, Exploring, Optimizing, Validating, FinalTest, Retired
  - _Requirements: 18.1_

- [ ] 2.2 Amend `StrategyIdentity` with `Stage` and `Hypothesis` fields
  - Default Stage=Exploring for old JSON, Hypothesis nullable

  - _Requirements: 18.1, 18.4_
- [ ] 2.3 Amend `StrategyVersion` with `TotalTrialsRun` and `SealedTestSet`
  - int TotalTrialsRun=0, DateRangeConstraint? SealedTestSet=null

  - _Requirements: 19.4, 20.6_
- [x] 2.4 Amend `StudyRecord` with partial result fields

  - bool IsPartial=false, int CompletedCount=0, int TotalCount=0
  - _Requirements: 7.5_
- [ ] 2.5 Add new entries to `StudyType` enum
  - AnchoredWalkForward, CombinatorialPurgedCV, RegimeSegmentation

  - _Requirements: 20.1_
- [x] 2.6 Add `WalkForwardMode` enum to Application/Research

  - Rolling, Anchored
  - _Requirements: 20.2_

- [ ] 2.7 Add `DataFileRecord`, `ValidationStatus`, `IDataFileRepository`
  - New Application/DataFiles folder, IHasId, standard CRUD
  - _Requirements: 3.7, 3.8, 15.1_
- [x] 2.8 Write unit tests for domain model amendments

  - JSON round-trip tests for all amended records
  - _Requirements: 18.1, 20.6, 7.5, 3.7_

- [x] 3. Application layer new services and DI wiring


- [x] 3.1 Add `IProgressReporter` interface to Application/Research


  - void Report(int current, int total, string label)
  - _Requirements: 5.5_
- [x] 3.2 Add `IReportExporter` interface to Application/Export


  - ExportMarkdownAsync, ExportTradeCsvAsync, ExportEquityCsvAsync, ExportJsonAsync
  - _Requirements: 6.8_
- [x] 3.3 Implement `DsrCalculator` in Application/Metrics


  - Pure static function, Bailey and Lopez de Prado 2014 formula
  - _Requirements: 19.1, 19.2_
- [x] 3.4 Implement `MinBtlCalculator` in Application/Metrics


  - Pure static function, returns min bars for 95% confidence
  - _Requirements: 19.6_
- [x] 3.5 Implement `ResearchChecklistService` in Application/Research


  - Queries repos to compute 8-item checklist with ConfidenceLevel
  - _Requirements: 18.5, 18.6_
- [x] 3.6 Add `SealedTestSetViolationException` and `FinalValidationUseCase`


  - Application/Engine, loads version, validates sealed set, runs with bypass flag
  - _Requirements: 20.7, 20.9, 20.10_
- [x] 3.7 Implement `BackgroundStudyService` abstraction


  - Singleton, StartStudyAsync/CancelStudy/GetActiveStudies, OnProgress/OnCompleted events
  - Concrete impl in Web host creates DI scope per study
  - _Requirements: 9.1, 9.2, 9.3_
- [x] 3.8 Amend `RunScenarioUseCase` with trial-count lifecycle


  - Increment TotalTrialsRun, snapshot into TrialCount
  - Rules: completed=+1, sweep=+N, validation fail=skip, runtime fail=+1
  - _Requirements: 19.4, 19.5_

- [x] 3.9 Amend `RunScenarioUseCase` with DSR enrichment

  - Compute DSR after trial count set, only for completed runs with non-null Sharpe

  - _Requirements: 19.1, 19.2_
- [x] 3.10 Wire all new services into DI and startup hooks


  - Register all new interfaces/implementations
  - Call MigrationService.MigrateIfNeededAsync on startup
  - _Requirements: 4.5, 15.1_
- [x] 3.11 Write unit tests for new services


  - DSR, MinBTL, ResearchChecklist, SealedTestSet, FinalValidation, trial count tests
  - _Requirements: 19.1, 19.6, 18.5, 20.7, 20.9, 19.4_

- [x] 4. Infrastructure layer implementations




- [ ] 4.1 Implement `JsonDataFileRepository` in Infrastructure/Persistence
  - JSON file store at datafiles/{fileId}.json


  - _Requirements: 15.1, 15.2_
- [x] 4.2 Implement `MigrationService` in Infrastructure/Persistence


  - Check migration_v4.lock, create synthetic Imported strategy, link orphaned results

  - Non-destructive, failure logged, does not crash app
  - _Requirements: 4.1, 4.2, 4.3, 4.5, 4.6_

- [ ] 4.3 Implement `MarkdownReportExporter` in Infrastructure/Export
  - Config table, metrics table with DSR, robustness notes, trade log

  - _Requirements: 6.2_
- [x] 4.4 Implement `CsvReportExporter` in Infrastructure/Export






  - Trade log CSV and equity curve CSV with correct columns


  - _Requirements: 6.3, 6.5_





- [x] 4.5 Implement `JsonReportExporter` in Infrastructure/Export

  - Full BacktestResult serialized, round-trips without data loss
  - _Requirements: 6.4_


- [ ] 4.6 Implement `BlazorProgressReporter` in Infrastructure/Progress
  - Wraps Action callback for Blazor UI updates

  - _Requirements: 5.5_
- [x] 4.7 Write integration tests for Infrastructure

  - JsonDataFileRepository CRUD, MigrationService idempotency, export round-trip
  - _Requirements: 15.3, 4.1, 4.5, 6.2_













- [x] 5. Anchored walk-forward implementation

- [ ] 5.1 Add `WalkForwardMode` parameter to `WalkForwardWorkflow`
  - Anchored: training start fixed, end advances. Rolling: existing behaviour


  - _Requirements: 20.3, 20.4_




- [x] 5.2 Write unit tests for anchored walk-forward

  - Verify training start does not move in anchored mode, rolling unchanged
  - _Requirements: 20.3, 20.4_


- [x] 6. Sealed test set, final validation, and cancellation persistence





- [x] 6.1 Add sealed test set enforcement to study orchestration


  - Check overlap with StrategyVersion.SealedTestSet before dispatching studies

  - _Requirements: 20.7_

- [ ] 6.2 Implement `FinalValidationUseCase` end-to-end
  - Load version, validate sealed set, run on sealed range, mark DevelopmentStage.FinalTest
  - _Requirements: 20.9, 20.10_
- [x] 6.3 Persist partial results on study cancellation






  - MC: store completed paths. WF: store completed windows. Sweep: store completed combos
  - Set IsPartial, CompletedCount, TotalCount, StudyStatus.Cancelled
  - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5_

- [ ] 6.4 Add sealed test set prompt to Strategy Builder Step 2
  - Slider for last N% as sealed set, store on StrategyVersion.SealedTestSet


  - _Requirements: 20.6_

- [x] 6.5 Add Final Validation action to Strategy Detail


  - One-time action with confirmation dialog, warning on post-validation param changes

  - _Requirements: 20.9, 20.10_
- [x] 6.6 Write tests for sealed test set and cancellation flows

  - Overlap blocked, final validation bypasses, stage updated, partial persistence

  - _Requirements: 20.7, 20.9, 20.10, 7.2_


- [x] 7. Web UI — Error handling and failure states

- [x] 7.1 Update `ResultDetail.razor` with failure state UI

  - Failed banner, collapsed error, View Full Error, Copy to Clipboard, Re-run actions

  - _Requirements: 2.5, 2.6, 2.7_
- [x] 7.2 Update Strategy Detail run list with failed run badge

  - _Requirements: 2.5_


- [ ] 8. Web UI — Execution progress and background studies
- [x] 8.1 Create `ExecutionStatusBar.razor` component

  - Subscribe to BackgroundStudyService events, show progress/completion

  - _Requirements: 9.2, 9.3_
- [x] 8.2 Add `ExecutionStatusBar` to `MainLayout.razor`

  - 32px bar below MudMainContent, visible during active execution

  - _Requirements: 9.2_
- [x] 8.3 Add inline progress for single-run backtests

  - Spinner with bar count, elapsed time, Cancel button

  - _Requirements: 5.1, 5.3_



- [x] 9. Web UI — Export functionality

- [ ] 9.1 Create `ExportMenu.razor` shared component
  - Dropdown: Markdown, JSON, CSV trade log, CSV equity curve

  - _Requirements: 6.9_
- [x] 9.2 Add `ExportMenu` to `ResultDetail.razor`

  - Replace existing Export Markdown button
  - _Requirements: 6.1, 6.9_
- [ ] 9.3 Add study export actions to study detail pages
  - MC: path summary CSV. WF: per-window OOS CSV
  - _Requirements: 6.6, 6.7_

- [ ] 10. Web UI — Research lifecycle and checklist
- [ ] 10.1 Create `ResearchChecklist.razor` shared component
  - 8-item checklist with icons, Confidence Level badge
  - _Requirements: 18.5, 18.6_
- [ ] 10.2 Add `ResearchChecklist` to `StrategyDetail.razor`
  - Call ResearchChecklistService.ComputeAsync on load
  - _Requirements: 18.5_
- [ ] 10.3 Add `DevelopmentStage` badge to strategy cards and Dashboard
  - _Requirements: 18.2_
- [x] 10.4 Add Research Pipeline section to Dashboard

  - Group strategies by DevelopmentStage, show counts


  - _Requirements: 18.3, 10.1_
- [ ] 10.5 Add Hypothesis field to Strategy Builder and Strategy Detail
  - Builder step 5 prompt, Strategy Detail display with soft prompt if null
  - _Requirements: 18.4_

- [ ] 11. Web UI — DSR and overfitting warnings
- [ ] 11.1 Add DSR KPI tile to `ResultDetail.razor`
  - Next to raw Sharpe, tooltip, warning badge when DSR < 0.95
  - _Requirements: 19.2, 19.3_
- [ ] 11.2 Add trial count and MinBTL warnings
  - Strategy Detail: TotalTrialsRun with contextual warning
  - Run Detail: MinBTL warning when bar count insufficient
  - _Requirements: 19.5, 19.6, 19.7_
- [ ] 11.3 Add 2D parameter surface heatmap to Sensitivity Study Detail
  - Colour-coded Sharpe heatmap, fragility index, 20% peak area highlight
  - _Requirements: 19.8_

- [ ] 12. Web UI — Analytics expansion
- [ ] 12.1 Add expanded metrics to `ResultDetail.razor`
  - DSR, Profit Factor, Expectancy, Recovery Factor, Calmar, Avg Holding, Consecutive Loss Max
  - _Requirements: 21.1_
- [ ] 12.2 Create `MaeMfeScatter.razor` and `MaeMfeHistogram.razor`
  - Scatter: MAE vs MFE per trade, colour-coded. Histograms: distributions
  - _Requirements: 21.2, 21.3_
- [ ] 12.3 Create `RunComparisonDelta.razor` shared component
  - Parameter changes, performance deltas with arrows, contextual warnings
  - _Requirements: 21.4_

- [ ] 13. Web UI — Data Files enhancements
- [ ] 13.1 Enhance `Data.razor` with add-file flow and validation details
  - Add by path or drag-and-drop, auto-detect metadata, per-rule validation
  - _Requirements: 3.1, 3.2, 3.3, 3.4_
- [ ] 13.2 Update Strategy Builder Step 2 to filter by validated files only
  - _Requirements: 3.5_
- [ ] 13.3 Add delete confirmation for files used by existing runs
  - _Requirements: 3.6_

- [ ] 14. Web UI — Settings enhancements
- [ ] 14.1 Add directory path configuration to Settings page
  - Results, Data Files, Rule Packs directories with folder picker
  - _Requirements: 10.2_
- [ ] 14.2 Add execution timeout configuration to Settings page
  - Study Timeout (minutes), Single-Run Timeout (seconds)
  - _Requirements: 10.2_
- [ ] 14.3 Add About section to Settings page
  - Version, build date, changelog link, migration check
  - _Requirements: 10.2_

- [ ] 15. Web UI — Study detail enhancements
- [ ] 15.1 Add PBO placeholder tile to Monte Carlo Study Detail
  - Disabled tile with "CPCV scheduled for V4.1" message
  - _Requirements: 20.5_
- [ ] 15.2 Add anchored/rolling mode toggle to Walk-Forward Study Detail
  - _Requirements: 14.2, 20.3_
- [ ] 15.3 Add `PartialStudyBanner.razor` component
  - Partial banner and amber badge in Research Explorer
  - _Requirements: 7.2, 7.3, 7.4, 7.6_

- [ ] 16. Responsive layout
- [ ] 16.1 Add responsive breakpoints to layout
  - Sidebar to top nav at 768-1023px, KPI 2x2 reflow, table horizontal scroll
  - _Requirements: 11.1, 11.2_

- [ ] 17. Accessibility and keyboard shortcuts
- [ ] 17.1 Create `KeyboardShortcutOverlay.razor` component
  - ? opens overlay, wire N/R/Esc shortcuts via JS interop
  - _Requirements: 12.1_
- [ ] 17.2 Accessibility pass — aria labels and focus management
  - aria-label on icon buttons and status badges, visible focus rings, form labels
  - _Requirements: 12.2, 12.4, 12.5, 12.7_
- [ ] 17.3 Accessibility pass — chart data tables and colour independence
  - Show data table toggle beneath charts, colour not sole status indicator
  - _Requirements: 12.3, 12.6_

- [ ] 18. CPCV placeholder
- [ ] 18.1 Add placeholder `CpcvStudyHandler` in Application/Research
  - Registered in DI, UI does not expose in study creation flows
  - Direct invocation returns "CPCV is scheduled for V4.1" via NotImplementedException
  - _Requirements: 20.5_

- [x] 19. Strategy Version Execution Window (V4 Amendment)
- [x] 19.1 Add `Timeframe` nullable field to `ScenarioConfig` in Core
  - Backwards-compatible trailing parameter, null for legacy configs
  - _Requirements: 22.8_
- [x] 19.2 Implement `ExecutionWindowEditor` in Application/Engine
  - Validate, GetCurrentWindow, EstimateBarCount static methods
  - Validates against data file bounds and sealed test set
  - _Requirements: 22.2, 22.3, 22.5, 22.6_
- [x] 19.3 Add Execution Window card and Edit dialog to StrategyDetail.razor
  - Summary card with Timeframe/Start/End/Est. Bars
  - Edit dialog with validation, FinalTest warning
  - Save updates StrategyVersion.BaseScenarioConfig
  - _Requirements: 22.1, 22.2, 22.7, 22.9_
- [x] 19.4 Write unit tests for ExecutionWindowEditor
  - Valid range, start after end, out of data file range, sealed set conflict
  - Legacy compat, timeframe inference, bar count estimation
  - _Requirements: 22.5, 22.6, 22.8_
