# V3 Product & UX Specification — TradingResearchEngine

## 1. Current-State Review

### Architecture Summary
The repo implements a clean-architecture event-driven backtesting engine with 7 projects: Core (domain), Application (use cases, workflows, strategies, prop-firm), Infrastructure (CSV/HTTP providers, JSON persistence, reporters), Cli, Api, Web (Blazor Server), plus UnitTests and IntegrationTests. V2 fixed critical numeric correctness bugs. V2.1 added execution realism, session awareness, position sizing policies, research robustness workflows, and experiment metadata.

### What V2/V2.1 Do Well
- Engine correctness is now trustworthy (look-ahead bias eliminated, Sharpe/Sortino from equity curve, continuous mark-to-market)
- Clean separation: strategies emit signals only, sizing/risk/execution are separate concerns
- Rich research workflows: Monte Carlo, walk-forward, parameter stability, sensitivity analysis, regime segmentation
- ExperimentMetadata enables reproducibility
- Event trace mode enables debugging

### Gaps from a User-Product Perspective
1. **No strategy identity model.** A strategy is just a `StrategyType` string on `ScenarioConfig`. There is no concept of "this is my SMA crossover strategy with these parameters that I've been iterating on." Every run is disconnected.
2. **No study concept.** A Monte Carlo simulation of 1000 paths is stored as a `MonteCarloResult` but has no parent concept linking it to the strategy it evaluated. Walk-forward, sensitivity, and parameter sweeps are similarly orphaned.
3. **Prop firm evaluation is too generic.** `FirmRuleSet` has 7 fields. Real prop firms have challenge phases, scaling plans, profit targets, consistency windows, and rules that vary by account size. The current model can't represent FTMO vs MyFundedFX vs TopStep differences.
4. **No guided creation path.** Users must construct `ScenarioConfig` JSON or use the Blazor form. There's no wizard, no templates, no progressive disclosure.
5. **Results are flat.** `BacktestResult` is a single record. There's no hierarchy: strategy → version → runs → studies → evaluations.
6. **No strategy versioning.** Changing a parameter creates a new run but there's no way to track that "v2 of my strategy changed the fast period from 10 to 12."

---

## 2. UX/Product Critique

### Creating a Strategy
- Current: pick a strategy type from a dropdown, fill in parameters, pick data source. No guidance on what parameters mean, no defaults explained, no templates.
- Problem: novice users have no idea what `adfRecheckInterval` means or why `entryStdDevs = 2` is a reasonable default.

### Understanding What a Strategy Is
- Current: a strategy is a C# class implementing `IStrategy`. The UI shows it as a string name.
- Problem: users think of a strategy as "my mean reversion idea for EURUSD daily" — a concept that spans multiple parameter sets, runs, and studies. The system has no model for this.

### Launching a Backtest
- Current: fill out a form, click Run. Works but no progressive disclosure — all fields visible at once.
- Problem: 20+ fields on ScenarioConfig is overwhelming. Most users want: pick strategy, pick data, click run.

### Comparing Runs
- Current: multi-select results → comparison table. Works for quants.
- Problem: no concept of "compare these two versions of my strategy" or "compare my strategy across firms."

### Interpreting Robustness
- Current: Monte Carlo shows P10/P50/P90 and ruin probability. Walk-forward shows per-window results.
- Problem: no summary verdict. Users want "is this strategy robust?" not "here are 47 numbers."

### Evaluating Prop Firm Suitability
- Current: pick a firm rule set, evaluate. Shows pass/fail and violated rules.
- Problem: no challenge phase modelling, no "how close am I to failing?", no firm comparison, no specific firm presets.

---

## 3. Proposed Domain/Product Model

### Core Product Concepts

| Concept | Definition | Identity |
|---|---|---|
| Strategy | A named, user-owned research concept. "My EURUSD mean reversion." | `StrategyId` |
| Strategy Version | A specific parameter configuration of a strategy. | `StrategyVersionId` |
| Builder Draft | An in-progress strategy being configured in the wizard. | `BuilderDraftId` |
| Strategy Template | A pre-built starting point (e.g. "SMA Crossover for Forex Daily"). | `TemplateId` |
| Run | A single backtest execution producing a `BacktestResult`. | `RunId` (existing) |
| Scenario | The full configuration for a run (existing `ScenarioConfig`). | `ScenarioId` (existing) |
| Study | A research workflow execution (Monte Carlo, walk-forward, sweep, sensitivity). | `StudyId` |
| Prop Firm Rule Pack | A specific firm's challenge rules (FTMO 100k, MyFundedFX 200k, etc.). | `RulePackId` |
| Prop Evaluation | An evaluation of a strategy version against a rule pack. | `EvaluationId` |
| Report View | A rendered summary of a run, study, or evaluation. | (derived) |

### Relationships

```
Strategy (1) ──→ (N) StrategyVersion
StrategyVersion (1) ──→ (N) Run
StrategyVersion (1) ──→ (N) Study
Study (1) ──→ (N) Run (e.g. Monte Carlo = 1 study with 1000 internal paths)
StrategyVersion (1) ──→ (N) PropEvaluation
PropEvaluation (1) ──→ (1) RulePack
Template (1) ──→ (N) Strategy (via "create from template")
BuilderDraft (1) ──→ (0..1) Strategy (when saved)
```

Key insight: a Monte Carlo study with 1000 paths is ONE study, not 1000 runs. The study references the source run and contains the simulation result. Walk-forward is ONE study containing N windows.

---

## 4. Strategy Identity and Hierarchy

### Canonical Identity Model

```csharp
// Application layer — not Core
public sealed record StrategyIdentity(
    string StrategyId,          // "my-eurusd-mean-reversion"
    string StrategyName,        // "EURUSD Mean Reversion"
    string StrategyType,        // "mean-reversion" (maps to IStrategy)
    DateTimeOffset CreatedAt,
    string? Description);

public sealed record StrategyVersion(
    string StrategyVersionId,   // "my-eurusd-mean-reversion-v3"
    string StrategyId,          // parent
    int VersionNumber,
    Dictionary<string, object> Parameters,
    DateTimeOffset CreatedAt,
    string? ChangeNote);        // "Increased lookback from 20 to 30"

public sealed record StudyRecord(
    string StudyId,
    string StrategyVersionId,   // parent
    StudyType Type,             // MonteCarlo, WalkForward, Sensitivity, ParameterSweep, Realism
    DateTimeOffset CreatedAt,
    string? SourceRunId);       // the base run this study was derived from

public enum StudyType { MonteCarlo, WalkForward, Sensitivity, ParameterSweep, Realism, ParameterStability }
```

### Terminology Rules
- "Strategy" = the user's research concept (persisted, named, versioned)
- "Version" = a specific parameter set within a strategy
- "Run" = a single backtest execution (produces `BacktestResult`)
- "Study" = a research workflow execution (Monte Carlo, walk-forward, etc.)
- "Evaluation" = a prop firm assessment of a strategy version
- Never call a Monte Carlo path a "run." It's a "simulation path" within a study.
- Never call a walk-forward window a "run." It's a "window" within a study.

---

## 5. Proposed UX Architecture

### Primary Navigation (Sidebar)

```
📊 Dashboard
📚 Strategy Library
🔬 Research Explorer
🏢 Prop Firm Lab
📁 Data Files
⚙️ Settings
```

### Screen Responsibilities

| Screen | Purpose |
|---|---|
| Dashboard | Recent activity, quick-launch, headline metrics from last run |
| Strategy Library | Browse, create, version, and manage strategies |
| New Strategy (wizard) | Guided strategy creation from template or scratch |
| Strategy Detail | All versions, runs, studies, evaluations for one strategy |
| Run Detail | Full backtest result with equity curve, trades, metrics |
| Research Explorer | Launch and view studies (MC, WF, sensitivity, sweep) |
| Study Detail | Study-specific results (MC paths, WF windows, sensitivity matrix) |
| Prop Firm Lab | Evaluate strategies against firm rule packs |
| Firm Comparison | Compare same strategy across multiple firms |
| Data Files | Manage CSV data, preview, validate |
| Settings | Config values, registered strategies, defaults |

### User Flow: Idea → Evaluation

```
1. Dashboard → "New Strategy" button
2. Guided Builder: pick template → configure → preview → save
3. Strategy Library → select strategy → "Run Backtest"
4. Run Detail → review metrics → "Run Study" (Monte Carlo / Walk-Forward)
5. Study Detail → review robustness → "Evaluate for Prop Firm"
6. Prop Firm Lab → select firm → see pass/fail + near-breach warnings
```

---

## 6. Wireframes / Screen Layouts

### Strategy Library

```
┌─────────────────────────────────────────────────────┐
│ Strategy Library                    [+ New Strategy] │
├─────────────────────────────────────────────────────┤
│ Search: [____________]  Filter: [All Types ▼]       │
├─────────────────────────────────────────────────────┤
│ ┌─────────────────────────────────────────────────┐ │
│ │ 📈 EURUSD Mean Reversion          v3 (latest)  │ │
│ │    mean-reversion · Daily · 12 runs · 3 studies │ │
│ │    Last run: 2h ago · Sharpe 1.42 · DD 8.3%    │ │
│ │    [View] [Run] [Study] [Evaluate]              │ │
│ └─────────────────────────────────────────────────┘ │
│ ┌─────────────────────────────────────────────────┐ │
│ │ 📈 BTC Breakout H4                v1            │ │
│ │    donchian-breakout · H4 · 3 runs · 1 study   │ │
│ │    Last run: 1d ago · Sharpe 0.87 · DD 15.1%   │ │
│ │    [View] [Run] [Study] [Evaluate]              │ │
│ └─────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────┘
```

### Guided Strategy Builder

```
┌─────────────────────────────────────────────────────┐
│ New Strategy                              Step 2/5  │
├─────────────────────────────────────────────────────┤
│ ① Template  ② Market  ③ Rules  ④ Execution  ⑤ Save │
│    ✓          ●                                     │
├─────────────────────────────────────────────────────┤
│ Market & Data                                       │
│                                                     │
│ Symbol:     [EURUSD     ▼]                          │
│ Timeframe:  [Daily      ▼]  (auto-sets BarsPerYear) │
│ Data Source: [CSV File   ▼]                          │
│ Date Range: [2020-01-01] to [2024-12-31]            │
│                                                     │
│ ℹ️ Daily bars use 252 bars/year for annualisation    │
│                                                     │
│              [← Back]  [Next: Entry/Exit Rules →]   │
└─────────────────────────────────────────────────────┘
```

### Backtest Result Screen

```
┌─────────────────────────────────────────────────────┐
│ Run: EURUSD Mean Reversion v3 — 2024-01-15 14:32   │
│ Status: ✅ Completed · 847 bars · 23 trades · 1.2s  │
├─────────────────────────────────────────────────────┤
│ ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌─────────┐│
│ │Sharpe    │ │Max DD    │ │Win Rate  │ │K-Ratio  ││
│ │  1.42    │ │  8.3%    │ │  61%     │ │  0.34   ││
│ │ ▲ good   │ │ ▲ low    │ │          │ │ ▲ smooth││
│ └──────────┘ └──────────┘ └──────────┘ └─────────┘│
├─────────────────────────────────────────────────────┤
│ [Equity Curve] [Drawdown] [Trades] [P&L] [Config]  │
│ ┌─────────────────────────────────────────────────┐ │
│ │ 📈 Equity curve chart (full width)              │ │
│ │    with drawdown overlay below                  │ │
│ └─────────────────────────────────────────────────┘ │
├─────────────────────────────────────────────────────┤
│ ⚠️ Robustness: Not yet evaluated. [Run Monte Carlo] │
│ 🏢 Prop Firm: Not yet evaluated. [Evaluate]         │
│                                                     │
│ [Run Study ▼]  [Compare]  [Export Markdown]         │
└─────────────────────────────────────────────────────┘
```

### Prop Firm Evaluation Screen

```
┌─────────────────────────────────────────────────────┐
│ Prop Firm Evaluation                                │
│ Strategy: EURUSD Mean Reversion v3                  │
├─────────────────────────────────────────────────────┤
│ Firm: [FTMO 100k ▼]  Challenge: [Phase 1 ▼]        │
├─────────────────────────────────────────────────────┤
│ ┌──────────┐ ┌──────────┐ ┌──────────┐            │
│ │ Verdict  │ │Pass Prob │ │Breakeven │            │
│ │ ⚠️ RISKY │ │  62%     │ │ 4 months │            │
│ └──────────┘ └──────────┘ └──────────┘            │
├─────────────────────────────────────────────────────┤
│ Rule Compliance                                     │
│ ┌───────────────────────────────────┬──────┬──────┐│
│ │ Rule                              │Status│Margin││
│ ├───────────────────────────────────┼──────┼──────┤│
│ │ Max Daily Drawdown (5%)           │ ✅   │ 2.1% ││
│ │ Max Total Drawdown (10%)          │ ⚠️   │ 1.7% ││
│ │ Min Trading Days (10)             │ ✅   │ +13  ││
│ │ Profit Target (10%)               │ ✅   │ +2.3%││
│ │ Consistency (30% max single trade)│ ❌   │-4.2% ││
│ └───────────────────────────────────┴──────┴──────┘│
│                                                     │
│ ⚠️ Near-breach: Total drawdown within 1.7% of limit │
│ ❌ Violation: Consistency rule breached              │
│                                                     │
│ [Compare Firms]  [Run Monte Carlo for Pass Rate]    │
└─────────────────────────────────────────────────────┘
```

---

## 7. Guided Strategy Builder Design

### Approach: Step-Based Wizard with Advanced Escape Hatch

The builder uses a 5-step wizard for novice users, with an "Advanced Mode" toggle that reveals the full ScenarioConfig form for power users.

### Steps

1. **Choose Template** — Pick from pre-built templates (SMA Crossover, Mean Reversion, RSI, Bollinger Bands, Breakout, Donchian, Stationary MR, Macro Regime) or start from scratch. Each template shows a one-line description and typical use case.

2. **Market & Data** — Select symbol, timeframe, data source, date range. Timeframe auto-sets `BarsPerYear`. Data source shows available CSV files or Dukascopy download option.

3. **Entry/Exit Rules** — Template-specific parameter form with sensible defaults, tooltips, and validation. For SMA Crossover: fast period, slow period. For Mean Reversion: lookback, entry std devs. Each parameter shows its default, valid range, and a plain-English explanation.

4. **Execution Assumptions** — Realism profile selector (Fast Research / Standard / Conservative) with progressive disclosure. Clicking "Customize" reveals: slippage model, commission model, initial cash, risk-free rate, position sizing policy. Defaults are pre-filled from the selected profile.

5. **Review & Save** — Summary of all choices. Name the strategy. Optional description. Save as new strategy (creates StrategyIdentity + StrategyVersion). Option to immediately run a backtest.

### Templates

Each template is a `StrategyTemplate` record in Application:

```csharp
public sealed record StrategyTemplate(
    string TemplateId,
    string Name,
    string Description,
    string StrategyType,           // maps to IStrategy
    string TypicalUseCase,         // "Forex daily trend following"
    Dictionary<string, object> DefaultParameters,
    string RecommendedTimeframe,   // "Daily", "H4", etc.
    ExecutionRealismProfile RecommendedProfile);
```

### Advanced Mode

A toggle at the top of the builder switches to a single-page form showing all `ScenarioConfig` fields. Changes in advanced mode are reflected back in the wizard steps if the user switches back.

### Validation

Each step validates before allowing "Next." Invalid fields show inline errors. The Review step shows a complete validation summary. The builder never produces an invalid `ScenarioConfig`.

---

## 8. Backtest and Analytics UX

### KPI Ordering (Above the Fold)

| Priority | Metric | Why |
|---|---|---|
| 1 | Sharpe Ratio | Primary risk-adjusted return measure |
| 2 | Max Drawdown | Primary risk measure |
| 3 | Win Rate | Intuitive for all users |
| 4 | K-Ratio | Equity curve quality |

### Secondary Diagnostics (Visible in Tabs)

- Sortino, Calmar, Profit Factor, Expectancy, Recovery Factor
- Average Win/Loss, Max Consecutive Wins/Losses
- Average Holding Period, Total Trades

### Advanced Diagnostics (Behind Expanders)

- Regime segmentation breakdown
- MAE/MFE distributions
- Event trace (when enabled)
- Execution assumptions detail

### Robustness Warnings

The UI should show automatic warnings when:
- Sharpe > 3.0 (suspiciously high — possible overfitting)
- Total trades < 30 (insufficient sample size)
- K-Ratio < 0 (equity curve declining)
- Max drawdown > 20% (high risk)
- Fragility score > 0.7 (parameter island)
- Cost sensitivity > 50% Sharpe degradation (execution-dependent edge)

These appear as colored badges next to the relevant metric.

### Monte Carlo Interpretation

Show a "Robustness Verdict" card:
- 🟢 Robust: P10 end equity > start equity AND ruin probability < 5%
- 🟡 Marginal: P10 end equity > start equity BUT ruin probability 5-15%
- 🔴 Fragile: P10 end equity < start equity OR ruin probability > 15%

### Walk-Forward Interpretation

Show composite OOS equity curve prominently. Below it:
- Average OOS Sharpe (with comparison to in-sample Sharpe)
- Parameter drift score (low = stable, high = unstable)
- Worst window drawdown

---

## 9. Prop Firm Evaluation UX

### Firm-Specific Rule Packs

Replace the generic `FirmRuleSet` with a richer model:

```csharp
public sealed record PropFirmRulePack(
    string RulePackId,
    string FirmName,
    string ChallengeName,        // "FTMO 100k Phase 1", "MyFundedFX 200k Evaluation"
    decimal AccountSizeUsd,
    IReadOnlyList<ChallengePhase> Phases,
    decimal? PayoutSplitPercent,
    decimal? ScalingThresholdPercent,
    IReadOnlyList<string> UnsupportedRules,  // rules we can't model
    string? Notes);

public sealed record ChallengePhase(
    string PhaseName,            // "Phase 1", "Phase 2", "Funded"
    decimal ProfitTargetPercent,
    decimal MaxDailyDrawdownPercent,
    decimal MaxTotalDrawdownPercent,
    int MinTradingDays,
    int? MaxTradingDays,
    decimal? ConsistencyRulePercent,
    bool TrailingDrawdown);      // trailing vs static drawdown
```

### Pre-Built Firm Packs

Ship with JSON rule packs for:
- FTMO (25k, 50k, 100k, 200k — Phase 1, Phase 2, Funded)
- MyFundedFX (50k, 100k, 200k — Evaluation, Funded)
- TopStep (50k, 100k, 150k — Trading Combine, Funded)
- The5ers (20k, 60k, 100k — Hyper Growth, Bootcamp)

Users can create custom rule packs.

### Pass/Fail Presentation

For each rule:
- ✅ Passed (with margin: "2.1% below limit")
- ⚠️ Near-breach (within 20% of limit)
- ❌ Failed (with overshoot: "4.2% over limit")

### Pass Probability

"Likely to pass" is computed from Monte Carlo simulation of the strategy against the rule pack. This is different from "historically passed" — it's a forward-looking probability estimate.

Show:
- Monte Carlo pass rate (e.g. "62% of 1000 simulated paths passed all rules")
- Confidence interval
- Which rule is the most common failure mode

### Firm Comparison

A table comparing the same strategy across multiple firms:

```
┌──────────────┬──────────┬──────────┬──────────┐
│              │ FTMO 100k│ MFF 200k │ TopStep  │
├──────────────┼──────────┼──────────┼──────────┤
│ Pass Rate    │ 62%      │ 71%      │ 55%      │
│ Profit Target│ ✅ +2.3% │ ✅ +4.1% │ ⚠️ +0.8% │
│ Max DD       │ ⚠️ 1.7%  │ ✅ 3.2%  │ ❌ -1.1% │
│ Consistency  │ ❌        │ N/A      │ ✅       │
│ Min Days     │ ✅        │ ✅       │ ✅       │
│ Best Fit     │          │ ★        │          │
└──────────────┴──────────┴──────────┴──────────┘
```

---

## 10. Kiro Spec Additions

### Proposed New Requirements

#### REQ-V3-01: Strategy Identity Model

**User Story:** As a user, I want to name, version, and track my strategies as persistent research concepts, so that I can iterate on a strategy over time without losing history.

**Acceptance Criteria:**
1. THE Application layer SHALL define `StrategyIdentity` and `StrategyVersion` records.
2. EACH `BacktestResult` SHALL be linked to a `StrategyVersionId`.
3. THE UI SHALL display strategies as named entities with version history.
4. CREATING a new version SHALL preserve all previous versions and their runs.

#### REQ-V3-02: Study as First-Class Concept

**User Story:** As a user, I want Monte Carlo, walk-forward, and sensitivity analyses grouped as "studies" linked to a strategy version, so that I can see all research for a strategy in one place.

**Acceptance Criteria:**
1. THE Application layer SHALL define a `StudyRecord` linking a research workflow execution to a `StrategyVersionId`.
2. A Monte Carlo study with 1000 paths SHALL be ONE study, not 1000 runs.
3. THE UI SHALL display studies grouped under their parent strategy version.

#### REQ-V3-03: Guided Strategy Builder

**User Story:** As a novice user, I want a step-by-step wizard to create a strategy from a template, so that I can start backtesting without understanding all configuration options.

**Acceptance Criteria:**
1. THE UI SHALL provide a 5-step wizard: Template → Market → Rules → Execution → Save.
2. EACH template SHALL pre-fill sensible defaults for all parameters.
3. AN advanced mode toggle SHALL reveal the full ScenarioConfig form.
4. THE builder SHALL validate each step before allowing progression.

#### REQ-V3-04: Prop Firm Rule Packs with Challenge Phases

**User Story:** As a user, I want to evaluate my strategy against specific prop firm challenge models with phase-by-phase rules, so that I can see exactly where my strategy passes or fails.

**Acceptance Criteria:**
1. THE `PropFirmRulePack` SHALL support multiple `ChallengePhase` records per firm.
2. EACH phase SHALL define: profit target, max daily DD, max total DD, min/max trading days, consistency rule, trailing vs static drawdown.
3. THE system SHALL ship with pre-built rule packs for FTMO, MyFundedFX, TopStep, and The5ers.
4. THE UI SHALL show per-rule pass/fail with margin and near-breach warnings.

#### REQ-V3-05: Robustness Verdict

**User Story:** As a user, I want an automatic robustness verdict based on Monte Carlo results, so that I can quickly assess whether a strategy is trustworthy.

**Acceptance Criteria:**
1. THE UI SHALL display a robustness verdict (Robust / Marginal / Fragile) based on P10 end equity and ruin probability.
2. THE UI SHALL show automatic warnings for suspicious metrics (Sharpe > 3, trades < 30, K-Ratio < 0, fragility > 0.7).

### Proposed Design Amendments

- Add `StrategyIdentity`, `StrategyVersion`, `StudyRecord` to Application layer (not Core)
- Add `PropFirmRulePack` and `ChallengePhase` to Application/PropFirm (replacing or extending `FirmRuleSet`)
- Add `StrategyTemplate` records to Application
- Add `IStrategyRepository` (Application interface, Infrastructure implementation) for strategy persistence
- BacktestResult gains optional `StrategyVersionId` field

### Proposed Task Groups

| Group | Tasks |
|---|---|
| Domain Model | Add StrategyIdentity, StrategyVersion, StudyRecord, StrategyTemplate to Application |
| Prop Firm Model | Add PropFirmRulePack, ChallengePhase, pre-built firm JSON packs |
| Strategy Builder | Implement 5-step wizard in Blazor Web |
| Strategy Library | Implement strategy list, detail, version history pages |
| Research Explorer | Implement study launcher, study detail pages |
| Prop Firm Lab | Implement evaluation screen, firm comparison, Monte Carlo pass rate |
| Robustness UX | Implement verdict cards, warning badges, progressive disclosure |
| Persistence | Add IStrategyRepository, IStudyRepository (JSON file implementations) |

---

## 11. Implementation Roadmap

### Phase 1: Domain Model (Immediate — Application layer only)
- Add `StrategyIdentity`, `StrategyVersion`, `StudyRecord`, `StrategyTemplate` records
- Add `PropFirmRulePack`, `ChallengePhase` records
- Add `IStrategyRepository`, `IStudyRepository` interfaces
- Add JSON file implementations in Infrastructure
- Ship pre-built firm rule pack JSON files
- Wire into DI

### Phase 2: Strategy Builder UI
- Implement 5-step wizard in Blazor
- Implement template selection with previews
- Implement advanced mode toggle
- Connect to `RunScenarioUseCase`

### Phase 3: Strategy Library + Research Explorer
- Strategy list page with version history
- Run detail page (upgrade existing)
- Study launcher (Monte Carlo, walk-forward, sensitivity from strategy context)
- Study detail pages

### Phase 4: Prop Firm Lab
- Evaluation screen with phase-by-phase rules
- Near-breach warnings and margin display
- Firm comparison table
- Monte Carlo pass rate computation

### Phase 5: Robustness UX
- Verdict cards on run detail
- Warning badges
- Progressive disclosure for advanced analytics

---

## 12. Risks and Guardrails

| Risk | Guardrail |
|---|---|
| Strategy/run identity collapse | `StrategyIdentity` lives in Application, not Core. `BacktestResult` keeps its existing `RunId`. Link is via optional `StrategyVersionId`. |
| Overloading Core with product concepts | All new product concepts (Strategy, Study, Template, RulePack) live in Application. Core remains engine-only. |
| Generic analytics confusing users | Progressive disclosure: headline metrics above fold, diagnostics in tabs, advanced behind expanders. |
| Misleading Monte Carlo | Always show "X% of N paths" not just "pass rate." Show confidence interval. Label as "simulated probability" not "guaranteed." |
| Prop evaluation mixed with engine truth | PropFirm remains Application-layer consumer. Rule packs are data, not engine logic. |
| Builder too abstract | Templates provide concrete starting points. Each parameter has a tooltip. Defaults are always filled. |
| Builder too technical | Advanced mode is opt-in. Wizard steps use plain English. Technical terms are explained inline. |


---

## Addendum: Clarifications and Extensions

### Scope Clarification

This application is single-user, local / single-tenant. There is no user management, authentication, or team features in V3. All `StrategyId`, `StrategyVersionId`, `StudyId`, etc. are owned by a single user context. All data is stored locally (JSON files in a configurable directory). Multi-user and cloud deployment are explicitly out of scope.

---

### Data Files & Market Data

#### Data File Discovery and Validation

Data files are discovered by convention: the application scans a configurable `DataDirectory` (default: `data/` relative to the working directory, plus `samples/data/` for bundled examples). Files are not "uploaded" — they are placed in the directory and discovered on next scan.

**Validation rules:**
- Required columns for bar data: `Timestamp`, `Open`, `High`, `Low`, `Close`, `Volume` (case-insensitive)
- Timestamps must be in ascending order; out-of-order rows are flagged as warnings
- Missing data (null/empty cells) is flagged per-row; files with >5% missing rows show a warning badge
- Format auto-detection: the existing `CsvFormatConverter` detects Yahoo/TradingView/MetaTrader formats and offers conversion

**Template association:** Each `StrategyTemplate` has a `RecommendedTimeframe` field. When a user selects a template in the builder, the data file picker filters to show files matching that timeframe (if detectable from filename or metadata). The user can override.

#### REQ-V3-06: Data File Management

**User Story:** As a user, I want to browse, preview, and validate my data files, so that I can ensure my backtests use clean data.

**Acceptance Criteria:**
1. THE Data Files screen SHALL list all CSV files in the configured data directories.
2. EACH file SHALL show: filename, detected format, row count, file size, date range (if parseable), and validation status (✅ Valid / ⚠️ Warnings / ❌ Errors).
3. THE user SHALL be able to preview the first 20 rows of any file.
4. THE user SHALL be able to trigger format conversion (non-engine format → engine format).
5. WHEN a file has validation errors, THE UI SHALL show a detailed error list with row numbers.

#### Data File Wireframe

```
┌─────────────────────────────────────────────────────┐
│ Data Files                          [Scan Directory] │
├─────────────────────────────────────────────────────┤
│ Directory: C:\data\                 [Change]         │
├──────────────────────┬────────┬──────┬──────┬───────┤
│ File                 │ Format │ Rows │ Range│Status │
├──────────────────────┼────────┼──────┼──────┼───────┤
│ EURUSD_Daily.csv     │ Engine │ 2610 │ 10yr │ ✅    │
│ BTCUSD_H4.csv        │ Yahoo  │ 8760 │ 1yr  │ ⚠️ 3  │
│ SPY_Daily.csv         │ Engine │ 5040 │ 20yr │ ✅    │
├──────────────────────┴────────┴──────┴──────┴───────┤
│ Selected: BTCUSD_H4.csv                             │
│ ┌─────────────────────────────────────────────────┐ │
│ │ Preview (first 20 rows)                         │ │
│ │ Timestamp    | Open   | High   | Low    | Close │ │
│ │ 2024-01-01   | 42100  | 42500  | 41800  | 42300 │ │
│ │ ...                                             │ │
│ └─────────────────────────────────────────────────┘ │
│ ⚠️ 3 warnings: rows 142, 891, 1203 have missing Vol │
│ [Convert to Engine Format]  [Validate]              │
└─────────────────────────────────────────────────────┘
```

#### Data File Selection in Builder (Step 2)

In the Strategy Builder's Market & Data step, the data file picker shows:
- A dropdown of validated files filtered by recommended timeframe
- File metadata (rows, date range) inline
- A "Browse all files" link to show unfiltered list
- A validation badge next to each file
- If no files match the timeframe, a message: "No files found for [timeframe]. [Go to Data Files]"

---

### Error Handling & Failure States

#### REQ-V3-07: Error Handling for Runs and Studies

**User Story:** As a user, I want clear feedback when a backtest or study fails, so that I can understand what went wrong and fix it.

**Acceptance Criteria:**

1. WHEN a backtest run fails (engine exception, invalid config, bad data), THE UI SHALL:
   - Show an inline error banner on the Run Detail screen with the error summary
   - Show a toast notification if the user is on a different screen
   - Persist the failed run record with `Status = Failed` and error summary in `BacktestResult`
   - Offer actions: "Edit Config & Retry", "View Error Details", "Delete Failed Run"

2. WHEN a study fails mid-way (Monte Carlo abort, walk-forward window failure), THE UI SHALL:
   - Show an inline error banner on the Study Detail screen
   - Persist the study record with a `Status = Failed` or `Status = Incomplete` label
   - If partial results exist (e.g. 500 of 1000 MC paths completed), store them with a clear "Incomplete (500/1000)" label
   - Offer actions: "Retry", "View Partial Results", "Delete"

3. WHEN a prop firm evaluation fails (missing required metrics, no trades), THE UI SHALL:
   - Show an inline error on the Evaluation screen explaining which data is missing
   - Not persist a failed evaluation (evaluations are computed on-demand, not stored)
   - Offer action: "Run Backtest First" if no source run exists

4. ALL error messages SHALL be user-friendly (no raw exception stack traces). A "View Technical Details" expander SHALL show the full error for debugging.

---

### Backtest & Study Execution UX

#### REQ-V3-08: In-Progress Execution States

**User Story:** As a user, I want to see progress while a backtest or study is running, and be able to cancel if needed.

**Acceptance Criteria:**

1. WHILE a backtest is running, THE UI SHALL show:
   - A progress indicator on the Run Detail screen ("Running… 45% complete" if bar count is known, otherwise "Running…")
   - A disabled "Run" button on the Strategy Detail and Builder screens
   - A "Cancel" button that triggers `CancellationToken` cancellation
   - The run status on the Dashboard's recent activity list

2. WHILE a study is running, THE UI SHALL show:
   - Progress as "Path X of N" (Monte Carlo) or "Window X of N" (walk-forward)
   - A "Cancel" button
   - Partial results are not shown until the study completes or is cancelled

3. WHEN a run or study is cancelled:
   - The result SHALL be stored with `Status = Cancelled`
   - Partial equity curve data (up to the cancellation point) SHALL be preserved
   - The UI SHALL show "Cancelled at bar X of Y" or "Cancelled at path X of N"

4. ONLY one run or study SHALL execute at a time per strategy. Attempting to launch a second SHALL show a warning: "A run is already in progress for this strategy."

---

### Migration & Backward Compatibility

#### REQ-V3-09: Legacy Run Migration

**User Story:** As a user with existing V2 backtest results, I want my old results to remain accessible when I upgrade to V3, without being forced to retroactively organize them into strategies.

**Acceptance Criteria:**

1. EXISTING `BacktestResult` records without a `StrategyVersionId` SHALL appear in a "Legacy Runs" section of the Run History page.
2. THE UI SHALL visually distinguish legacy runs (no strategy link) from V3 strategy-linked runs (e.g. a "Legacy" badge).
3. THE user SHALL be able to "Adopt" a legacy run into a strategy by:
   - Creating a new strategy from the run's config
   - Or linking the run to an existing strategy version
4. LEGACY runs SHALL NOT be deleted or hidden by the V3 upgrade.
5. ALL existing result browsing, comparison, and export functionality SHALL continue to work for legacy runs.

---

### Export & Reporting

#### REQ-V3-10: Export and Reporting

**User Story:** As a user, I want to export backtest results in multiple formats, so that I can share findings and keep records.

**Acceptance Criteria:**

1. THE Run Detail screen SHALL offer an "Export" dropdown with options:
   - **Markdown Report** — structured document with: scenario metadata, headline KPIs, equity curve summary, trade summary, robustness warnings, execution assumptions
   - **CSV Trade Log** — all closed trades with: Symbol, EntryTime, ExitTime, EntryPrice, ExitPrice, Quantity, Direction, GrossPnl, Commission, NetPnl, ReturnOnRisk
   - **JSON Result** — full `BacktestResult` serialised to JSON (for programmatic consumption)

2. EXPORTS SHALL be saved to a configurable export directory (default: `exports/`).
3. FILE naming SHALL follow the pattern: `{strategy-name}_v{version}_{run-date}.{ext}` (e.g. `eurusd-mean-reversion_v3_2024-01-15.md`). Legacy runs use `legacy_{scenario-id}_{run-date}.{ext}`.
4. THE Markdown report SHALL include:
   - Strategy name and version (if linked)
   - Scenario configuration summary
   - Headline metrics table (Sharpe, MaxDD, WinRate, K-Ratio, ProfitFactor)
   - Equity curve summary (start equity, end equity, peak, trough)
   - Trade count and average holding period
   - Robustness warnings (if any)
   - Execution assumptions (fill mode, slippage model, commission model, realism profile)

---

### Custom Prop Firm Rule Pack Editor

#### REQ-V3-11: Custom Rule Pack Editor

**User Story:** As a user, I want to create and edit custom prop firm rule packs, so that I can model firms not included in the pre-built packs.

**Acceptance Criteria:**

1. THE Prop Firm Lab SHALL include a "Manage Rule Packs" section.
2. THE user SHALL be able to:
   - Create a new rule pack from scratch
   - Duplicate an existing pack (including pre-built ones) as a starting point
   - Edit custom packs (pre-built packs are read-only but can be duplicated)
   - Delete custom packs

3. THE Rule Pack Editor SHALL include:
   - Firm name and challenge name fields
   - Account size
   - A dynamic list of challenge phases (add/remove)
   - Per-phase fields: phase name, profit target %, max daily DD %, max total DD %, min trading days, max trading days (optional), consistency rule % (optional), trailing drawdown toggle
   - Payout split % and scaling threshold % (optional)
   - Unsupported rules text field (for rules the system can't model)
   - Notes field

4. VALIDATION SHALL enforce:
   - At least one challenge phase
   - Numeric fields within reasonable ranges (0-100% for percentages, >0 for account size)
   - Required fields per phase (profit target, max daily DD, max total DD)

5. THE selection UI SHALL visually distinguish built-in packs (🔒 icon) from custom packs (✏️ icon).

#### Rule Pack Editor Wireframe

```
┌─────────────────────────────────────────────────────┐
│ Edit Rule Pack: My Custom Firm                      │
├─────────────────────────────────────────────────────┤
│ Firm Name:      [My Custom Firm    ]                │
│ Challenge Name: [50k Evaluation    ]                │
│ Account Size:   [$50,000           ]                │
│ Payout Split:   [80%               ]                │
├─────────────────────────────────────────────────────┤
│ Challenge Phases                    [+ Add Phase]   │
│ ┌─────────────────────────────────────────────────┐ │
│ │ Phase 1: Evaluation                    [Remove] │ │
│ │ Profit Target: [8%]  Max Daily DD: [5%]         │ │
│ │ Max Total DD:  [10%] Min Days: [5]              │ │
│ │ Max Days: [30]  Consistency: [30%]              │ │
│ │ ☐ Trailing Drawdown                             │ │
│ └─────────────────────────────────────────────────┘ │
│ ┌─────────────────────────────────────────────────┐ │
│ │ Phase 2: Funded                        [Remove] │ │
│ │ Profit Target: [—]   Max Daily DD: [5%]         │ │
│ │ Max Total DD:  [10%] Min Days: [0]              │ │
│ │ ☐ Trailing Drawdown                             │ │
│ └─────────────────────────────────────────────────┘ │
├─────────────────────────────────────────────────────┤
│ Unsupported Rules: [Weekend holding restrictions   ]│
│ Notes:             [Based on XYZ firm Q1 2025 rules]│
├─────────────────────────────────────────────────────┤
│                    [Cancel]  [Save Rule Pack]       │
└─────────────────────────────────────────────────────┘
```

---

### Dashboard & Settings

#### REQ-V3-12: Dashboard

**User Story:** As a user, I want a dashboard showing my recent activity and key metrics, so that I can quickly resume work.

**Acceptance Criteria:**

1. THE Dashboard SHALL show:
   - **Recent Strategies** — last 5 strategies with latest version, last run date, headline Sharpe
   - **Recent Runs** — last 5 runs with status, strategy name, Sharpe, MaxDD
   - **Quick Actions** — "New Strategy", "Run Last Strategy Again", "Open Last Run"
   - **Robustness Summary** — if the most recent run has a Monte Carlo study, show the verdict (Robust/Marginal/Fragile)

2. THE Dashboard SHALL NOT show overwhelming detail — it is a landing page, not an analytics screen.

#### REQ-V3-13: Settings

**User Story:** As a user, I want to configure global defaults, so that new strategies and runs use my preferred settings.

**Acceptance Criteria:**

1. THE Settings screen SHALL expose:
   - **Data directory** path (where CSV files are discovered)
   - **Export directory** path (where reports are saved)
   - **Default execution realism profile** (FastResearch / Standard / Conservative)
   - **Default initial cash** amount
   - **Default risk-free rate**
   - **Default position sizing policy**
   - **Registered strategies** list (read-only, from StrategyRegistry)

2. SETTINGS SHALL be persisted to a JSON file in the `.kiro/` directory or app data folder.
3. CHANGES to settings SHALL take effect on the next run (not retroactively).

---

### Accessibility & Power-User Considerations

#### REQ-V3-14: Keyboard Navigation and Accessibility

**User Story:** As a user, I want to navigate the core flows using keyboard only, so that I can work efficiently without a mouse.

**Acceptance Criteria:**

1. THE Strategy Builder wizard SHALL support:
   - Tab/Shift-Tab to move between fields within a step
   - Enter to advance to the next step (when current step is valid)
   - Escape to go back one step
   - All form controls accessible via keyboard

2. THE following keyboard shortcuts SHALL be supported (when not in a text input):
   - `Ctrl+N` — New Strategy
   - `Ctrl+R` — Run backtest (on Strategy Detail or Run Detail)
   - `Ctrl+E` — Export (on Run Detail)

3. ALL interactive elements SHALL have visible focus indicators.
4. ALL charts and data visualizations SHALL have text alternatives (e.g. metric values displayed alongside charts, not only in chart tooltips).
5. COLOR SHALL NOT be the sole indicator of status — icons (✅ ⚠️ ❌) SHALL accompany color-coded badges.

---

### Updated Implementation Roadmap

The original 5-phase roadmap is extended:

**Phase 1: Domain Model + Data Management** (Immediate)
- StrategyIdentity, StrategyVersion, StudyRecord, StrategyTemplate
- PropFirmRulePack, ChallengePhase, pre-built firm JSON packs
- IStrategyRepository, IStudyRepository (JSON file implementations)
- Data file validation and management service
- Legacy run migration support (optional StrategyVersionId on BacktestResult)
- Settings persistence

**Phase 2: Strategy Builder + Library** (After Phase 1)
- 5-step wizard with templates
- Strategy library page with version history
- Data file picker integration
- Advanced mode toggle
- Keyboard navigation

**Phase 3: Run & Study Execution UX** (After Phase 2)
- In-progress states with progress indicators
- Cancellation support
- Error handling (inline banners, toast, retry)
- Failed/incomplete run persistence

**Phase 4: Research Explorer + Analytics** (After Phase 3)
- Study launcher from strategy context
- Monte Carlo / walk-forward / sensitivity detail pages
- Robustness verdict cards and warning badges
- Progressive disclosure for advanced analytics
- Export (Markdown, CSV, JSON)

**Phase 5: Prop Firm Lab** (After Phase 4)
- Evaluation screen with phase-by-phase rules
- Custom rule pack editor
- Firm comparison table
- Monte Carlo pass rate computation
- Near-breach warnings
