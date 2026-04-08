# Implementation Plan — TradingResearchEngine V3 Product & UX

## Overview

V3 transforms the engine into a user-facing research product. All tasks are coding tasks. The engine (Core) is not modified. All new product concepts live in Application and Infrastructure. The Blazor Web project is the primary UI surface.

---

## Phase 1: Domain Model + Data Management

- [x] 1. Add strategy identity and versioning model to Application


- [x] 1.1 Create `StrategyIdentity` record in `Application/Strategy/`

  - _Requirements: REQ-V3-01_

- [x] 1.2 Create `StrategyVersion` record in `Application/Strategy/`

  - _Requirements: REQ-V3-01_

- [ ] 1.3 Create `IStrategyRepository` interface in `Application/Strategy/`
  - _Requirements: REQ-V3-01_

- [x] 1.4 Create `StrategyTemplate` record in `Application/Strategy/`



  - Ship default templates for all 6 existing strategies

  - _Requirements: REQ-V3-03_



- [x] 1.5 Add optional `StrategyVersionId` field to `BacktestResult`

  - _Requirements: REQ-V3-01, REQ-V3-09_



- [x] 2. Add study record model to Application



- [x] 2.1 Create `StudyRecord` record with `StudyType` and `StudyStatus` enums in `Application/Research/`

  - _Requirements: REQ-V3-02_

- [ ] 2.2 Create `IStudyRepository` interface in `Application/Research/`
  - _Requirements: REQ-V3-02_


- [x] 3. Add enriched prop firm model to Application



- [x] 3.1 Create `PropFirmRulePack` and `ChallengePhase` records in `Application/PropFirm/`

  - _Requirements: REQ-V3-04_




- [x] 3.2 Create `PhaseEvaluationResult` and `RuleResult` records

  - _Requirements: REQ-V3-04_

- [ ] 3.3 Update `PropFirmEvaluator` to evaluate against `PropFirmRulePack` with per-phase, per-rule results including margin and near-breach detection
  - _Requirements: REQ-V3-04_
- [ ] 3.4 Create pre-built firm rule pack JSON files for FTMO, MyFundedFX, TopStep, The5ers
  - _Requirements: REQ-V3-04_

- [ ] 4. Add Infrastructure persistence implementations
- [ ] 4.1 Implement `JsonStrategyRepository` in `Infrastructure/Persistence/`
  - _Requirements: REQ-V3-01_
- [ ] 4.2 Implement `JsonStudyRepository` in `Infrastructure/Persistence/`
  - _Requirements: REQ-V3-02_
- [ ] 4.3 Implement `DataFileService` in `Infrastructure/DataManagement/`
  - Scan directories, validate CSV, preview, format detection
  - _Requirements: REQ-V3-06_
- [ ] 4.4 Implement `SettingsService` in `Infrastructure/Settings/`
  - Load/save `AppSettings` from JSON
  - _Requirements: REQ-V3-13_

- [ ] 5. Wire new services into DI
- [ ] 5.1 Register `IStrategyRepository`, `IStudyRepository`, `DataFileService`, `SettingsService` in Infrastructure `ServiceCollectionExtensions`
  - _Requirements: REQ-V3-01, REQ-V3-02, REQ-V3-06, REQ-V3-13_
- [ ] 5.2 Register `StrategyTemplate` list in Application `ServiceCollectionExtensions`
  - _Requirements: REQ-V3-03_

- [ ] 6. Write unit tests for Phase 1
- [ ] 6.1 Test `StrategyIdentity` and `StrategyVersion` JSON round-trip
  - _Requirements: REQ-V3-01_
- [ ] 6.2 Test `PropFirmRulePack` multi-phase evaluation with margin and near-breach
  - _Requirements: REQ-V3-04_
- [ ] 6.3 Test `DataFileService` validation (valid CSV, missing columns, out-of-order timestamps)
  - _Requirements: REQ-V3-06_

---

## Phase 2: Strategy Builder + Library UI

- [ ] 7. Implement Strategy Library page
- [ ] 7.1 Create `StrategyLibrary.razor` — list strategies with version, run count, last Sharpe
  - _Requirements: REQ-V3-01_
- [ ] 7.2 Create `StrategyDetail.razor` — version history, linked runs, linked studies
  - _Requirements: REQ-V3-01, REQ-V3-02_
- [ ] 7.3 Implement "Adopt Legacy Run" flow — link existing BacktestResult to a strategy
  - _Requirements: REQ-V3-09_

- [ ] 8. Implement Guided Strategy Builder
- [ ] 8.1 Create `StrategyBuilder.razor` with 5-step wizard layout and step navigation
  - _Requirements: REQ-V3-03_
- [ ] 8.2 Implement Step 1: Template picker with descriptions and use cases
  - _Requirements: REQ-V3-03_
- [ ] 8.3 Implement Step 2: Market & Data — symbol, timeframe, data file picker with validation badges
  - _Requirements: REQ-V3-03, REQ-V3-06_
- [ ] 8.4 Implement Step 3: Entry/Exit Rules — template-specific parameter form with defaults and tooltips
  - _Requirements: REQ-V3-03_
- [ ] 8.5 Implement Step 4: Execution Assumptions — realism profile selector with progressive disclosure
  - _Requirements: REQ-V3-03_
- [ ] 8.6 Implement Step 5: Review & Save — summary, naming, save + optional immediate run
  - _Requirements: REQ-V3-03, REQ-V3-01_
- [ ] 8.7 Implement Advanced Mode toggle — full ScenarioConfig form
  - _Requirements: REQ-V3-03_
- [ ] 8.8 Implement keyboard navigation (Tab/Enter/Escape between steps)
  - _Requirements: REQ-V3-14_

---

## Phase 3: Run & Study Execution UX

- [ ] 9. Implement in-progress execution states
- [ ] 9.1 Add progress indicator to Run Detail page (bar count progress or spinner)
  - _Requirements: REQ-V3-08_
- [ ] 9.2 Add Cancel button with CancellationToken integration
  - _Requirements: REQ-V3-08_
- [ ] 9.3 Disable Run button while a run is active for the same strategy
  - _Requirements: REQ-V3-08_
- [ ] 9.4 Show cancelled/incomplete status with partial data
  - _Requirements: REQ-V3-08_

- [ ] 10. Implement error handling UI
- [ ] 10.1 Add inline error banner component for failed runs/studies
  - _Requirements: REQ-V3-07_
- [ ] 10.2 Add toast notification for failures when user is on a different screen
  - _Requirements: REQ-V3-07_
- [ ] 10.3 Add "Edit & Retry" and "View Error Details" actions on failed runs
  - _Requirements: REQ-V3-07_
- [ ] 10.4 Persist failed/incomplete runs and studies with status labels
  - _Requirements: REQ-V3-07_

---

## Phase 4: Research Explorer + Analytics

- [ ] 11. Implement Research Explorer
- [ ] 11.1 Create `ResearchExplorer.razor` — launch studies from strategy context
  - _Requirements: REQ-V3-02_
- [ ] 11.2 Create `StudyDetail.razor` — study-type-specific result display
  - _Requirements: REQ-V3-02_
- [ ] 11.3 Link study records to strategy versions in the UI
  - _Requirements: REQ-V3-02_

- [ ] 12. Implement robustness verdict and warning badges
- [ ] 12.1 Add robustness verdict card (Robust/Marginal/Fragile) to Run Detail
  - _Requirements: REQ-V3-05_
- [ ] 12.2 Add automatic warning badges for suspicious metrics
  - _Requirements: REQ-V3-05_
- [ ] 12.3 Implement progressive disclosure: headline → secondary → advanced diagnostics
  - _Requirements: REQ-V3-05_

- [ ] 13. Implement export functionality
- [ ] 13.1 Add Export dropdown to Run Detail (Markdown, CSV trade log, JSON)
  - _Requirements: REQ-V3-10_
- [ ] 13.2 Implement Markdown report generator with structured sections
  - _Requirements: REQ-V3-10_
- [ ] 13.3 Implement CSV trade log export
  - _Requirements: REQ-V3-10_
- [ ] 13.4 Implement JSON result export
  - _Requirements: REQ-V3-10_

---

## Phase 5: Prop Firm Lab

- [ ] 14. Implement Prop Firm Evaluation screen
- [ ] 14.1 Update `PropFirmLab.razor` with rule pack selector (built-in + custom)
  - _Requirements: REQ-V3-04_
- [ ] 14.2 Implement phase-by-phase rule compliance table with pass/near-breach/fail + margin
  - _Requirements: REQ-V3-04_
- [ ] 14.3 Implement Monte Carlo pass rate computation against rule pack
  - _Requirements: REQ-V3-04_
- [ ] 14.4 Add near-breach warnings and violation details
  - _Requirements: REQ-V3-04_

- [ ] 15. Implement Firm Comparison
- [ ] 15.1 Create `FirmComparison.razor` — same strategy across multiple firms
  - _Requirements: REQ-V3-04_
- [ ] 15.2 Implement comparison table with per-rule status and "Best Fit" indicator
  - _Requirements: REQ-V3-04_

- [ ] 16. Implement Custom Rule Pack Editor
- [ ] 16.1 Create `RulePackEditor.razor` with dynamic phase list
  - _Requirements: REQ-V3-11_
- [ ] 16.2 Implement CRUD for custom rule packs (create, duplicate, edit, delete)
  - _Requirements: REQ-V3-11_
- [ ] 16.3 Implement validation (required fields, numeric ranges)
  - _Requirements: REQ-V3-11_
- [ ] 16.4 Visual distinction between built-in (🔒) and custom (✏️) packs
  - _Requirements: REQ-V3-11_

- [ ] 17. Update Dashboard and Settings
- [ ] 17.1 Update Dashboard with recent strategies, recent runs, quick actions, robustness summary
  - _Requirements: REQ-V3-12_
- [ ] 17.2 Update Settings with configurable defaults and persistence
  - _Requirements: REQ-V3-13_

- [ ] 18. Update Data Files page
- [ ] 18.1 Integrate `DataFileService` into Data Files page
  - _Requirements: REQ-V3-06_
- [ ] 18.2 Add validation status badges and error detail display
  - _Requirements: REQ-V3-06_
- [ ] 18.3 Add file preview with first 20 rows
  - _Requirements: REQ-V3-06_
