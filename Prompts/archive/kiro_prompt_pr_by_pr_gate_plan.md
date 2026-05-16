# Kiro Implementation Prompt — TradingResearchEngine Dev Branch, PR-by-PR Mandatory Gate Plan

You are working on the **Dev** branch of `jonathdb/TradingResearchEngine`.

Your task is to implement **all findings, fixes, cleanup items, test gaps, and expansion opportunities** identified in the review. You must do this with a **PR-by-PR gate plan**, where each gate corresponds to a coherent pull request boundary that can be reviewed, validated, and merged before the next gate begins.

The goal is still to implement everything. **Do not create a backlog. Do not leave placeholders, “future work,” passive scaffolding, or half-finished architecture migrations.** If an item is too large for one PR, split it across multiple adjacent PR gates in this prompt and complete it before the final gate is done.

## Operating principles

Follow the repository’s existing standards and patterns:
- `.NET 8`, `C# 12`, nullable enabled
- Architecture rule: `Core ← Application ← Infrastructure ← Web`
- Prefer immutable records for domain types
- Respect the `.kiro/steering/*` rules, especially `tech.md`, `domain-boundaries.md`, `testing-standards.md`, and `security-policies.md`
- Preserve backward compatibility unless this prompt explicitly authorizes cleanup/migration
- Prefer deletion of obsolete code/docs/spec text over keeping dead compatibility layers with no value
- Keep public XML docs accurate
- Keep seeded research workflows deterministic where a seed is provided

## Non-negotiable instruction

**Do not defer work because it is large. Break it into reviewable PR gates and finish it.**

Only leave something partially implemented inside a gate if:
1. it would violate architecture/security rules,
2. it would require an external dependency or service that is not available,
3. or it would create unacceptable regression risk that must be isolated into the very next PR gate.

If that happens, finish the remaining work in the next PR gate in this same prompt. Do not move it to backlog.

---

## Phase 0 — Read Before Coding

Before making changes, read and follow:
- `.kiro/steering/tech.md`
- `.kiro/steering/domain-boundaries.md`
- `.kiro/steering/testing-standards.md`
- `.kiro/steering/security-policies.md`
- `README.md`
- `CHANGELOG.md`

Also read the relevant specs before editing the related area:
- `.kiro/specs/v5-engine-usability-and-quant-upgrades/*`
- `.kiro/specs/v51-engine-fixes/*`
- `.kiro/specs/v6-engine-upgrades/*`
- `.kiro/specs/v7-implementation-plan/*`
- `.kiro/specs/v8-ai-export-paper-indicators-portfolio/*`
- `.kiro/specs/web-only-ux-overhaul/*`

Then produce an execution plan that maps every numbered item in this prompt to a PR gate below.

---

# PR Gate Rules

Each PR gate must satisfy all of the following before moving to the next one:
1. The code builds.
2. Relevant tests pass.
3. Any changed docs/specs/comments are updated in the same PR.
4. Obsolete code/comments/docs for that gate’s scope are removed in the same PR.
5. The PR scope stays coherent and reviewable; do not mix unrelated changes unless explicitly instructed by the gate.

Each PR gate output should include:
- proposed PR title
- summary of scope
- files likely to change
- tests to add/update
- docs/specs to update
- explicit merge criteria

---

## PR Gate 1 — Walk-Forward Correctness and Research Flow Safety

### Proposed PR title
`fix/research-walkforward-correctness-and-validation`

### Scope
Implement and finish all of the following in this PR:

#### 1.1 Make walk-forward perform real in-sample optimization
- Add proper parameter-grid support to walk-forward
- Update `WalkForwardOptions` and related request/UI models
- Reuse existing sweep schema/grid UI where sensible
- Make walk-forward select the best in-sample result and validate it OOS correctly
- Add strong validation for empty/invalid grids
- Add/update tests proving multiple IS combinations are evaluated and the chosen IS winner drives OOS validation

#### 1.4 Prevent impossible walk-forward runs before execution
- Add pre-run validation using data range + IS/OOS/step settings
- Show expected window count and insufficiency warnings in the UI
- Block execution when no valid window can be formed
- Add/update tests

#### 3.2 Add irreversible-action confirmation for final validation
- Add explicit confirmation UX before sealing/consuming the test set
- Explain the consequences clearly
- Disable or clearly relabel the action when already consumed
- Add/update tests

#### 3.5 Make the research checklist an active workflow guide
- Surface incomplete/failing items prominently
- Link checklist items directly to relevant workflows/pages where possible
- Explain why confidence is low, not only the final score/label
- Integrate checklist state into the final validation experience with clear gating or warnings
- Add/update tests for checklist logic and related UI/service behavior

### Merge criteria
- Walk-forward performs real IS optimization and correct OOS validation
- Invalid walk-forward setups are blocked before execution
- Final validation has a proper irreversible-action confirmation flow
- Checklist guidance is active and visible
- All related tests pass
- Docs/specs/comments for this flow are updated

---

## PR Gate 2 — Indicator/Builder Correctness and Beginner Defaults

### Proposed PR title
`fix/strategy-builder-indicators-and-realism-defaults`

### Scope
Implement and finish all of the following in this PR:

#### 1.2 Fix null/placeholder indicator catalog entries
- Fix `SkenderIndicatorCatalog` placeholder/null entries explicitly; no catalog entry may silently return `null`
- Remove all silent `null` indicator factories
- Fully implement missing indicator(s), or if genuinely unsupported, remove them from the catalog/UI and replace with explicit validation errors
- Ensure builders/pickers never advertise unsupported indicators
- Add/update tests

#### 1.3 Remove obsolete short-only/long-only drift
- Remove `LongOnlyGuard` if truly unnecessary
- Remove or rewrite stale XML docs/comments/spec text mentioning it as active runtime behavior
- Update tests/docs to match actual V6+ short-selling support

#### 3.1 Fix beginner-mode realism defaults
- Default beginner flows to a realistic profile such as `StandardBacktest` or equivalent
- Add explanatory copy in the builder
- Keep advanced overrides available
- Add/update tests

#### 7.x Additional gate-specific tests
- Add or update component/integration tests for builder flows changed by this PR
- Add negative coverage if indicator selection and validation paths changed materially

### Merge criteria
- No placeholder indicator behavior remains user-reachable
- Builder defaults are realistic
- Short-selling documentation/comments reflect actual behavior
- All related tests pass
- Docs/specs/comments updated

---

## PR Gate 3 — Performance, Concurrency, and Deterministic Research Execution

### Proposed PR title
`perf/research-workflows-and-portfolio-hotpaths`

### Scope
Implement and finish all of the following in this PR:

#### 2.1 Remove `Portfolio` hot-path allocation and counting overhead
- Fix repeated `Portfolio.Positions` / `ShortPositions` allocation behavior explicitly
- Replace repeated dictionary recreation with a safe cached or purpose-built snapshot strategy
- Keep internal state encapsulated; do not expose mutable collections directly
- Optimize `OpenPositionCount` and other hot-path repeated counts/access patterns where appropriate
- Preserve behavior exactly
- Add/update tests proving correctness and snapshot parity

#### 2.2 Eliminate nested parallelism oversubscription in walk-forward + sweep
- Introduce a proper global or hierarchical concurrency budgeting approach
- Ensure combined workflows stay bounded and deterministic
- Keep each run isolated
- Add/update tests around concurrency option propagation or scheduling behavior where practical

#### 2.3 Parallelize Monte Carlo fully and safely
- Address the current sequential implementation of `MonteCarloWorkflow` explicitly
- Parallelize `MonteCarloWorkflow` with bounded concurrency
- Preserve deterministic seeded behavior
- Preserve existing algorithm semantics, including block bootstrap
- Add/update tests for deterministic fixed-seed outputs and simulation count correctness

#### 2.4 Parallelize CPCV fully and safely
- Address the current sequential implementation of `CpcvStudyHandler` explicitly
- Parallelize `CpcvStudyHandler` with bounded concurrency
- Preserve correctness of progress reporting and result aggregation
- Avoid shared mutable-state bugs
- Add/update tests for deterministic aggregate behavior where feasible

#### 2.5 Parallelize parameter perturbation fully and safely
- Parallelize `ParameterPerturbationWorkflow`
- Preserve deterministic seeded jitter behavior
- Add/update tests for run count and deterministic behavior

#### 4.4 Improve progress estimation accuracy
- Add provider-aware or interval-aware bar-count estimation
- Prefer an explicit provider estimate API when feasible
- Keep estimation lightweight; do not force expensive full preloading
- Add/update tests for progress estimation behavior

### Merge criteria
- Portfolio hot-path correctness is preserved and allocations reduced
- Research workflows use bounded concurrency safely
- Seeded workflows remain deterministic
- Progress estimation is improved and tested
- All related tests pass
- Docs/comments for changed behavior updated

---

## PR Gate 4 — Configuration Canonicalization and Runtime Construction Consistency

### Proposed PR title
`refactor/config-normalization-and-strategy-construction`

### Scope
Implement and finish all of the following in this PR:

#### 4.1 Reduce or eliminate `ScenarioConfig` dual-schema drift
- Move the codebase to one canonical configuration path
- Preserve load compatibility if necessary, but normalize aggressively
- Prefer persisting canonical shape after load/save where safe
- Reduce raw legacy-field usage across runtime code
- Add/update tests for normalization/equivalence
- Update docs/specs to state the canonical model

#### 4.2 Unify strategy construction behind one creation path
- Move runtime strategy creation to one consistent factory/provider mechanism
- Remove or minimize reflection-based divergence paths
- Add/update tests ensuring built-in strategies instantiate consistently and correctly

#### 4.3 Replace scattered stringly-typed provider option access with typed config usage
- Move runtime consumers toward typed provider/data config usage
- Keep compatibility only at ingestion/boundary layers if necessary
- Update `DataHandler`, `WalkForwardWorkflow`, `CpcvStudyHandler`, and relevant providers
- Add/update tests for normalization/typing behavior

### Merge criteria
- There is one clear canonical configuration path
- Strategy construction is unified
- Major runtime config consumers use typed/normalized config instead of scattered string-key access
- All related tests pass
- Docs/specs/comments updated in the same PR

---

## PR Gate 5 — Persistence Hardening, Job Control, and AI Call Safety

### Proposed PR title
`hardening/jobs-persistence-ai-timeouts`

### Scope
Implement and finish all of the following in this PR:

#### 5.1 Add strong timeout/cancellation control to Gemini AI calls
- Enforce configurable timeout behavior on AI calls
- Use linked cancellation tokens properly
- Preserve retry behavior where appropriate, but handle timeout/cancel semantics correctly
- Update options/docs/config if needed
- Add/update tests via abstraction/mocks where practical

#### 5.2 Add robust retry/final-failure handling for jobs
- Introduce explicit retry policy, backoff, and/or final-failure state
- Prevent infinite retry loops
- Distinguish transient from terminal outcomes appropriately
- Keep user-visible error messages sanitized
- Add/update tests for job lifecycle behavior

#### 5.3 Add explicit SQLite/JSON consistency reconciliation
- Add startup/runtime consistency verification between JSON store and SQLite index
- Reconcile or repair divergence safely
- Log structured diagnostics when mismatches are found
- Add/update tests for divergence detection/recovery where practical

#### 5.4 Make paper-trading polling configurable
- Bind polling cadence through options/config
- Use sane defaults
- Update docs/config examples
- Add/update tests if repo patterns support it

### Merge criteria
- AI calls cannot hang indefinitely
- Jobs have explicit safe retry/failure behavior
- SQLite/JSON divergence handling is explicit and tested
- Paper-trading polling is configurable
- All related tests pass
- Relevant docs/config examples updated

---

## PR Gate 6 — Repository Cleanup and Documentation Alignment

### Proposed PR title
`chore/remove-stale-assets-and-align-docs`

### Scope
Implement and finish all of the following in this PR:

#### 6.1 Clean up prompt-history and internal AI artifact files
- Audit `Prompts/`
- Keep only prompt files intentionally used by production code
- Remove or relocate archival/internal prompt-engineering artifacts
- Update references if paths change

#### 6.2 Remove obsolete CLI/API/web-transition leftovers
- Audit `samples/`, old docs, and references to removed or obsolete entry points
- Remove or relocate obsolete assets
- Keep only intentionally retained samples/test assets, and document why they remain

#### 6.3 Update all affected docs/specs/comments immediately
- Update `README.md`
- Update `CHANGELOG.md`
- Update affected docs under `docs/`
- Update `.kiro/specs/*/design.md`, `requirements.md`, and `tasks.md` where implementation changed or tasks were completed
- Update XML docs on public APIs where still stale
- Remove stale comments that no longer reflect the code

#### 7.x Spec-task reconciliation work
- Audit relevant unchecked tasks in `.kiro/specs/*/tasks.md`
- Mark complete the tasks directly implemented in previous gates

### Merge criteria
- Stale prompt-history and obsolete transition-era assets are cleaned up
- Docs/specs/tasks are aligned with implemented reality
- No obsolete commentary remains for already-completed work

---

## PR Gate 7 — Research Product Expansion: Analytics, Reporting, and AI Loop

### Proposed PR title
`feat/research-analytics-reporting-and-ai-refinement`

### Scope
Implement and finish all of the following in this PR:

#### 8.1 Expand Monte Carlo robustness analysis beyond narrow trade-order resampling
- Make simulation mode explicit in the domain model and UI
- Keep current trade resample / block bootstrap modes
- Add at least one richer simulation mode beyond trade-order reshuffling, such as return-series or path-based simulation, implemented end-to-end
- Clearly explain method differences in the UI and docs
- Add tests for each mode

#### 8.2 Enrich walk-forward analytics substantially
- Add richer metrics and reporting for walk-forward studies, including where feasible:
  - percentage of OOS-profitable windows
  - concatenated/merged OOS equity curve
  - parameter drift/stability summary across windows
  - richer summary metrics and visual presentation
- Add tests for the new calculations

#### 8.4 Add trade anatomy analytics
- Add deeper trade diagnostics beyond flat logs
- Include meaningful analytics such as MAE/MFE, trade duration distribution, and/or annotated trade markers on result charts
- Prefer implementing more than one if data is already available
- Add tests and UI/docs updates

#### 8.5 Enforce correlation-aware portfolio constraints
- If `PortfolioRiskConfig` exposes correlation controls, enforce them at runtime in portfolio execution/selection behavior
- Convert misleading post-analysis-only fields into real runtime behavior where applicable
- Add tests proving enforcement behavior
- Update docs/UI text accordingly

#### 8.7 Add persistent comparison report generation
- Add a real comparison-report capability for run/strategy comparisons
- Use the existing reporting/export patterns where possible
- Support at least one durable artifact format such as Markdown or HTML
- Wire it into the comparison UI
- Add tests and docs

#### 8.8 Close the AI refinement loop with real backtest context
- Automatically include the latest relevant backtest metrics when refining a strategy via AI
- Summarize metrics intelligently to keep prompts compact
- Ensure the flow works in the relevant UI/dialog/service path
- Add tests around prompt construction / payload shaping where practical
- Update docs/UI text so the behavior is visible to users

#### 3.3 Make large sweep results usable
- Add paging, virtualization, or an equivalent robust solution
- Preserve charts and summaries
- Ensure large sweeps remain responsive

#### 3.4 Consolidate or clearly separate comparison pages
- Audit `Compare.razor` and `CompareRuns.razor`
- Consolidate to a single canonical flow where possible
- If both remain, roles must be clearly differentiated in route/title/navigation
- Remove obsolete/dead routes and update docs

### Merge criteria
- Research workflows provide materially richer analytics
- Comparison flows are coherent and can output a durable report
- AI refinement uses real backtest context
- Large result surfaces remain usable
- Correlation controls are enforced meaningfully at runtime
- All related tests pass
- Docs/UI text updated

---

## PR Gate 8 — Engine Capability Expansion and Export Validation

### Proposed PR title
`feat/multi-timeframe-engine-and-export-validation`

### Scope
Implement and finish all of the following in this PR:

#### 8.3 Add multi-timeframe strategy support
- Extend config/domain/engine abstractions to support secondary timeframe data
- Implement the feed and event plumbing needed for multi-timeframe execution
- Add at least one concrete end-to-end strategy or reference implementation using multiple timeframes
- Add tests covering multi-timeframe behavior
- Update docs and UI/configuration surfaces accordingly

#### 8.6 Validate exported strategy code properly
- Add validation of generated Pine/MQL exports
- Use the strongest feasible validation approach available in-repo without unsafe external dependence
- At minimum add robust structural/syntax heuristics plus regression tests; prefer deeper validation if practical
- Fail clearly when export output is invalid
- Add/update tests for all exporters

#### 7.x Additional review gaps to close in this PR
- Add negative tests for `ExpressionCompiler` / malformed composite conditions
- Add or complete any directly relevant unchecked tasks in `.kiro/specs/*/tasks.md` covered by this PR

### Merge criteria
- The engine can run a real multi-timeframe strategy end-to-end
- Export validation catches meaningful problems and is tested
- Negative parser/condition tests exist
- Docs/specs/UI/configuration updated
- All related tests pass

---

## Final PR Gate — Full Repository Alignment and Closure

### Proposed PR title
`chore/final-alignment-and-review-closure`

### Scope
Before declaring the work complete, do all of the following in the final PR gate if anything remains:
- Re-run full build and relevant test suites
- Sweep for stale comments, dead code, dead files, superseded docs
- Ensure all earlier gate docs/spec/task updates are present and consistent
- Ensure no reviewed item remains only as a note when it could have been implemented safely
- Add any missing glue/docs/tests needed to fully close the review

### Final completion requirements
You are not done until all of the following are true:
1. Every numbered item in this prompt has been completed through one or more PR gates above
2. The solution builds cleanly
3. Tests pass
4. Relevant docs/specs/tasks are updated
5. Obsolete code/docs introduced by prior behavior are removed
6. Expansion items are implemented as real product capabilities, not just noted

---

## Final deliverable format

When finished, provide:
1. A summary of work completed by PR gate
2. A mapping from each numbered item in this prompt to the PR gate and files/changes that implemented it
3. A list of tests added/updated
4. A list of docs/specs/tasks updated
5. A short note on any item that had to be split across adjacent PR gates, and why
6. A short note on any item that could not be completed fully, with the exact blocking reason

