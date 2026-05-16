<img src="https://r2cdn.perplexity.ai/pplx-full-logo-primary-dark%402x.png" style="height:64px;margin-right:32px"/>

# Before i asked if you could look at the raw data input. You also have me a great review with improvements of the application. I have  your response pasted below. Could you also create a prompt for implementation planing for kiro for these aspects now?

Yes — stepping back, the application is becoming much stronger as a research workflow product, not just a backtest runner. The overall architecture now covers the full lifecycle from data ingestion, strategy creation, versioning, runs, studies, robustness checks, exports, and final validation, while the newer strategy-library work improves the front door by making strategy selection hypothesis-driven instead of indicator-driven.design.md+2
That said, the biggest remaining opportunity is no longer raw feature coverage — it is workflow coherence. The specs are rich, but the UX still risks feeling like a collection of powerful pages rather than one guided research system. The best improvements now are the ones that reduce cognitive load, make next actions obvious, and connect tests, strategies, studies, and outcomes into a clearer narrative.design.md+3
Overall assessment
From an application perspective, the platform is well structured. The V4 architecture keeps domain and workflow logic primarily in Application, uses Infrastructure for persistence/export/migration concerns, and limits Core changes to a few carefully justified fields and a date-range constraint, which is a healthy boundary for long-term maintainability.[design.md](https://ppl-ai-file-upload.s3.amazonaws.com/web/direct-files/attachments/56830553/d0fa967d-a04d-49dd-b2eb-713e46c3b600/design.md)
From a product perspective, the application now spans four major user jobs: prepare data, define a strategy, run and study it, and decide whether it is robust enough to promote. The strategy-library curation work improves that flow by reducing redundant sample strategies and attaching family, hypothesis, and suggested-study metadata directly to templates, which should make the first-run experience much more coherent.requirements.md+1
What is already strong
The strongest part of the product is the research depth model. Development stages, hypothesis capture, sealed test sets, DSR, MinBTL, anchored walk-forward, regime segmentation, and the research checklist all push the app toward disciplined quantitative research instead of casual curve-fitting. That is a real differentiator, especially because the design ties these concepts to specific pages like Strategy Detail, Run Detail, Dashboard, and Study Detail rather than leaving them as hidden back-end concepts.tasks.md+1
The second big strength is that the strategy system is becoming more purposeful. The curated six-strategy catalog reduces duplication, preserves the few genuinely distinctive ideas like stationarity-aware mean reversion and regime rotation, and introduces descriptors that explain what each built-in strategy is for, what hypothesis it tests, and which studies are appropriate next. That is good both technically and pedagogically.tasks.md+2
The third strength is the test mindset in the specs. The V4 plan includes unit tests for domain model changes, calculators, migration-safe serialization, study orchestration, exports, and key end-to-end flows like sealed final validation; the strategy-library work also adds tests for each new strategy and descriptor behavior. That is a good foundation for evolving a complex research app safely.design.md+3
Biggest workflow gaps
The app still needs a stronger “happy path”
The application has many capabilities, but the user journey is still page-oriented rather than flow-oriented. A user can create data, build a strategy, run tests, inspect results, and launch studies, but the specs suggest these are mostly connected by navigation and buttons rather than by a clearly guided progression from idea → baseline run → robustness studies → final validation.design.md+1
The research checklist helps, but it appears mainly as a status summary on Strategy Detail. I would elevate it into a more active workflow controller: after each completed run or study, the UI should explicitly recommend the next best action, such as “Run Monte Carlo next,” “You’ve optimized enough — define a sealed test set,” or “Final validation is now available.” That would turn the app into a coach rather than just a toolbox.[design.md](https://ppl-ai-file-upload.s3.amazonaws.com/web/direct-files/attachments/56830553/d0fa967d-a04d-49dd-b2eb-713e46c3b600/design.md)
Strategy creation still appears slightly fragmented
The Builder is improving with family badges and hypothesis text, but the overall creation flow still sounds like a sequence of forms rather than a guided research setup. The strongest UX change you could make here is to turn strategy creation into a research brief:
Choose strategy family/template.
Review the hypothesis in plain language.
Select validated data.
Set a default test recipe.
Save with stage = Exploring and a visible “recommended first studies” summary.requirements.md+1
Right now, the metadata exists, but the specs do not yet fully exploit it to drive defaults and reduce decision fatigue.
Study launch is still button-heavy
The Strategy Detail design highlights suggested studies with stars, which is helpful, but this still sounds like a row of buttons plus some decoration. From a UX perspective, that is better than nothing, but it misses an opportunity to frame studies as a sequence of evidence-gathering steps.[design.md](https://ppl-ai-file-upload.s3.amazonaws.com/web/direct-files/attachments/56830553/0b6c9a0f-5a2c-4eb4-ac83-8df82b4a32d6/design.md)
A better pattern would be:
“Recommended next”
“Optional supporting studies”
“Locked until prerequisites complete” for things like final held-out testing.design.md+1
That would align much better with the research checklist and development stages.
UX/UI review
Dashboard
The Dashboard is carrying a lot of responsibility: research pipeline, strategy health, stage grouping, and likely recent activity. That is directionally good, but the risk is that it becomes an information wall. The most useful dashboard for this app is one that answers three questions immediately:
What is running right now?
Which strategies need my attention?
What is the next highest-value action?[design.md](https://ppl-ai-file-upload.s3.amazonaws.com/web/direct-files/attachments/56830553/d0fa967d-a04d-49dd-b2eb-713e46c3b600/design.md)
I would bias the dashboard toward:
Active executions/status at top.
“Needs attention” strategies next, such as failed runs, low DSR, incomplete checklist, or sealed test set not yet configured.
Research pipeline summary below that.tasks.md+1
Strategy Detail
This is clearly the product’s center of gravity, and it should become even more opinionated. The design already includes hypothesis, family badge, research checklist, suggested studies, total trial count, and warnings, which is excellent.design.md+1
What I would improve is the information hierarchy. Strategy Detail should likely have this top section order:
Strategy identity: name, family, stage, hypothesis.
Current health: last run result, DSR, trial count, checklist confidence.
Next action panel: recommended study or final validation step.
Historical runs and studies.
Supporting details and exports.design.md+1
That would keep the page decision-oriented instead of archive-oriented.
Result Detail
Run Detail / Result Detail is getting richer with failure states, DSR, export menu, expanded metrics, MAE/MFE, and warnings. This is good, but there is a real UI risk of metric overload.tasks.md+1
I would strongly consider structuring Result Detail into tabs or grouped cards:
Summary
Robustness
Trades
Exports / raw data[design.md](https://ppl-ai-file-upload.s3.amazonaws.com/web/direct-files/attachments/56830553/d0fa967d-a04d-49dd-b2eb-713e46c3b600/design.md)
The raw metric count is less important than whether the user can quickly tell:
Did this run make money?
Is it statistically trustworthy?
What kind of pain did it endure?
Should I continue researching it?[design.md](https://ppl-ai-file-upload.s3.amazonaws.com/web/direct-files/attachments/56830553/d0fa967d-a04d-49dd-b2eb-713e46c3b600/design.md)
Data Files
The Data Files flow is important because it gates everything else. The V4 work adds data file validation, metadata detection, and filtering to only validated files in the builder, which is exactly right.tasks.md+1
From a UX perspective, this page should feel more like an ingestion inbox than a file browser. The key improvements would be:
show validation state very prominently,
surface symbol/timeframe/date-range confidence clearly,
make “fix or replace this file” the obvious action when validation fails,
and show where the file is used, so deletion decisions are safer.kiro_spec_improvements_v2.md+1
Strategy system review
The curated strategy catalog is much better than the older demo-oriented set. It now covers distinct families — trend, mean reversion, breakout, regime-aware, and benchmark — and removes obvious overlap like multiple rolling-mean/oscillator variants and a weaker breakout duplicate.[requirements.md](https://ppl-ai-file-upload.s3.amazonaws.com/web/direct-files/attachments/56830553/e0819274-58a1-49a0-ba5e-7689c8dce3a1/requirements.md)
The main thing still missing is a slightly stronger distinction between sample strategy and user research object. Right now, templates, descriptors, and saved strategies are close, but the UX should make clear that:
templates are starting points,
saved strategies are user-owned research theses,
versions are experiments,
runs and studies are evidence.design.md+1
That conceptual framing would help users understand the model much faster.
Test coverage review
The test plans are good and unusually thoughtful for a product spec. The V4 plan covers calculators, record serialization, migration, study execution, exports, and sealed test set flows, while the strategy-library plan covers new strategies, descriptor lookup, and removal fallout.tasks.md+3
Still, from an overall application quality perspective, I think there are three testing gaps worth addressing:
Workflow integration tests: for example, create strategy from template → save hypothesis → run backtest → launch suggested study → update checklist → final validation unlocks. The current tests lean strong on component logic and infrastructure but could use more user-journey coverage.design.md+1
UI-state tests: especially around fallback states, missing descriptors, failed runs, partial studies, and empty dashboards. The design includes many states, and those are exactly where UX regressions usually happen.design.md+1
Regression tests for sequencing rules: for example, trial count increments, sealed test set enforcement, and no overwrite of user-authored hypothesis in clone/edit flows. Some of this is already planned, but it is important enough to treat as a first-class regression surface.tasks.md+1
What I would improve next
Here are the highest-value improvements I’d suggest after the current strategy work.
AreaImproveWhy
Workflow
Add a “Next recommended action” engine on Strategy Detail and Dashboard design.md+1
Converts the app from a toolbox into a guided research flow [design.md](https://ppl-ai-file-upload.s3.amazonaws.com/web/direct-files/attachments/56830553/d0fa967d-a04d-49dd-b2eb-713e46c3b600/design.md)
UX
Reframe studies as a staged progression, not just a set of launch buttons design.md+1
Reduces decision fatigue and aligns with research checklist/stages [design.md](https://ppl-ai-file-upload.s3.amazonaws.com/web/direct-files/attachments/56830553/d0fa967d-a04d-49dd-b2eb-713e46c3b600/design.md)
Strategy creation
Turn Builder into a research brief flow with stronger defaults design.md+1
Makes first-time setup faster and more hypothesis-driven [requirements.md](https://ppl-ai-file-upload.s3.amazonaws.com/web/direct-files/attachments/56830553/e0819274-58a1-49a0-ba5e-7689c8dce3a1/requirements.md)
Result UX
Group metrics into summary/robustness/trades/export sections [design.md](https://ppl-ai-file-upload.s3.amazonaws.com/web/direct-files/attachments/56830553/d0fa967d-a04d-49dd-b2eb-713e46c3b600/design.md)
Prevents KPI overload and improves scanability [design.md](https://ppl-ai-file-upload.s3.amazonaws.com/web/direct-files/attachments/56830553/d0fa967d-a04d-49dd-b2eb-713e46c3b600/design.md)
Testing
Add end-to-end user-journey tests across Builder → Run → Study → Validation design.md+1
Catches workflow regressions that unit tests miss [design.md](https://ppl-ai-file-upload.s3.amazonaws.com/web/direct-files/attachments/56830553/d0fa967d-a04d-49dd-b2eb-713e46c3b600/design.md)
Data UX
Treat Data Files as an ingestion/validation queue with usage visibility design.md+1
Improves trust in inputs and reduces fragile setups [kiro_spec_improvements_v2.md](https://ppl-ai-file-upload.s3.amazonaws.com/web/direct-files/attachments/56830553/9fb2c0e0-e068-4f83-ae99-16c06c3a18e0/kiro_spec_improvements_v2.md)
Practical recommendation
If you want the product to feel noticeably better without adding a lot of architectural scope, I would prioritize these three UX/product changes next:
Recommended next step panel on Strategy Detail and Dashboard, driven by ResearchChecklistService, DevelopmentStage, and descriptor SuggestedStudies.design.md+1
Study-launch redesign that groups actions into Recommended / Optional / Locked instead of one flat action bar.design.md+1
End-to-end workflow tests that validate the intended research journey, not just isolated services and records.tasks.md+1
Those three would improve usability disproportionately compared with their implementation cost.tasks.md+2
If you want, I can turn this into a structured review memo for Kiro with:
strengths,
risks,
UX/workflow improvements,
recommended next-phase tasks.

Here is a prompt you can give Kiro to drive implementation planning for these workflow/UX improvements:

***

**Prompt for Kiro — Implementation Planning: Research Workflow \& UX Coherence**

You have an updated V4 spec and strategy-library design that make the app much stronger as a research workflow product, not just a backtest runner. Architecture is solid and research depth is high, but there are still gaps in **workflow coherence** and UX that we now want to address.[^1][^2]

Please propose an implementation plan (requirements deltas + tasks) for the following areas:

***

### 1. “Next Recommended Action” engine

Goal: Turn the app from a toolbox into a guided research flow by surfacing explicit next steps on Strategy Detail and Dashboard.[^1]

Scope and constraints:

- Use existing concepts only: `ResearchChecklistService`, `DevelopmentStage` on `StrategyIdentity`, `StrategyDescriptor.SuggestedStudies`, `TotalTrialsRun`, DSR, MinBTL, sealed test set state, and study completion records.[^2][^3][^1]
- Design an **Application-level service** (e.g. `NextActionService`) that, given a `strategyVersionId`, returns a single recommended next action (or a small ordered list) such as:
    - “Run first baseline backtest”
    - “Run Monte Carlo robustness next”
    - “Configure sealed test set”
    - “Run anchored walk-forward”
    - “Run final held-out validation”
    - “No further actions recommended; strategy is in FinalTest or Retired stage.”
- The recommendation logic should combine:
    - Development stage,
    - Research checklist completion state,
    - Suggested studies from the strategy descriptor,
    - DSR and MinBTL warnings,
    - Trial budget (TotalTrialsRun) vs overfitting risk.[^3][^2][^1]

Deliverables you should define:

- New requirement section(s) under V4 for “Next Recommended Action”.
- The `NextAction` model (fields like `ActionType`, `Label`, `TargetRoute`, `Prerequisites`, `BlockedReason`).
- Application service interface and implementation outline.
- Dashboard and Strategy Detail UX changes:
    - a dedicated “Next recommended step” panel on Strategy Detail, near the top, showing one primary action plus 0–2 alternates.[^1]
    - a Dashboard section listing strategies that “Need attention” with their next recommended step.[^3][^1]

***

### 2. Study launch as staged progression (Recommended / Optional / Locked)

Goal: Reframe study launch from a row of buttons into a staged progression that aligns with the research checklist and development stages, reducing decision fatigue.[^1]

Scope and constraints:

- Keep existing `StudyType` and `StudyRecord` model; no changes to Core.[^3][^1]
- On Strategy Detail and Study-related pages, group studies into three buckets:
    - **Recommended next:** primary studies from `StrategyDescriptor.SuggestedStudies` that match the current stage and checklist gaps.[^2]
    - **Optional supporting:** other available studies that are useful but not required.
    - **Locked until prerequisites complete:** items like final held-out validation and any study that explicitly depends on sealed test set or other prerequisites.[^3][^1]
- Show why an item is locked (e.g. “Requires sealed test set” or “Requires at least one completed baseline run”) instead of simply disabling a button.

Deliverables you should define:

- Requirements delta describing the grouping model and prerequisite rules per `StudyType`.
- Application-level helper or mapping that returns for a `strategyVersionId`:
    - `RecommendedStudies[]`, `OptionalStudies[]`, `LockedStudies[]` with reasons.
- UI changes in `StrategyDetail.razor` (and `StudyDetail` if needed) to:
    - visually separate Recommended / Optional / Locked groups,
    - surface descriptor-based recommendations (existing SuggestedStudies) as stars or labels, but now inside these groups rather than as flat adornments.[^2]

***

### 3. Strategy Builder as “Research Brief” flow

Goal: Turn Strategy Builder from a sequence of forms into the creation of a **research brief** with strong defaults and clearer narrative.[^4][^1]

Scope and constraints:

- Build on the existing Strategy Library design: `StrategyDescriptor`, `StrategyFamily`, hypothesis text, recommended studies per strategy.[^5][^2]
- Reframe the steps so a new strategy feels like authoring a brief:

1. Choose strategy family/template (cards with family badge and descriptor).[^5][^2]
2. Review hypothesis in plain language; optionally tweak it.
3. Select validated data file (from Data Files only).
4. Choose a **default test recipe** (e.g. “Baseline + Monte Carlo” or “Baseline + Walk-forward for trend strategies”) inferred from descriptor and family.
5. Save strategy with:
        - `Stage = Exploring` by default for new strategies,
        - Hypothesis populated from descriptor and user edits,
        - Research checklist starting at zero.[^2][^3]

Constraints:

- Do not overwrite user-authored hypothesis on clone/edit flows (that rule already exists and must be preserved).[^5]
- Use only validated `DataFileRecord` entries for selection, as in the current specs.[^1][^3]

Deliverables you should define:

- New/updated requirements describing the Builder as a research-brief flow, including:
    - default `DevelopmentStage` and hypothesis behavior,
    - default test recipe behavior per strategy family.[^4][^2][^1]
- Changes to `StrategyBuilder.razor` steps and UI text to reflect this narrative.
- Any new helper types or services needed to:
    - resolve default test recipe from strategy descriptor,
    - prefill hypothesis and stage,
    - ensure data file selection is restricted to validated entries.[^2][^3][^1]

***

### 4. Result Detail metric grouping (Summary / Robustness / Trades / Exports)

Goal: Reduce metric overload on Run Detail / Result Detail by grouping information into meaningful sections that answer the key questions: did it make money, is it trustworthy, what kind of pain, should I continue.[^1]

Scope and constraints:

- No Core changes; use existing metrics, DSR, MinBTL outputs, MAE/MFE charts, and export mechanisms.[^3][^1]
- Propose a **tabbed or card-based layout** such as:
    - **Summary:** equity curve, key KPIs, DSR tile, MinBTL warning if any, high-level P\&L.[^1]
    - **Robustness:** DSR breakdown, MinBTL details, trial count, warnings, study links, maybe hooks to Monte Carlo and walk-forward results.[^3][^1]
    - **Trades:** trade list, trade-level MAE/MFE, histograms/scatter plots (existing components).[^1]
    - **Exports:** links to Markdown export, trade CSV, equity CSV, JSON export (using `IReportExporter`).[^3][^1]

Deliverables you should define:

- Requirements delta for Result Detail layout, including which metrics/widgets live in which section.
- Proposed changes to `ResultDetail.razor` to implement grouping with minimal disruption to existing components.
- Any small Application changes needed to provide grouped view models, if you feel that improves clarity.[^3][^1]

***

### 5. Data Files as ingestion/validation queue with usage visibility

Goal: Make Data Files feel like an **ingestion + validation inbox** rather than a passive file browser, emphasizing trust in inputs and safe deletion.[^1]

Scope and constraints:

- Build on the existing Data Files design: `DataFileRecord`, `ValidationStatus`, metadata detection, validation rules, and integration with Strategy Builder.[^3][^1]
- Improve the Data page to:
    - show validation state prominently,
    - clearly display symbol, timeframe, date range, and confidence,
    - surface “fix or replace this file” as the obvious action when validation fails,
    - show where each file is used (strategies / scenarios) so deletion decisions are informed.[^1]
- Assume the Market Data acquisition workflow will feed into Data Files but **do not** redesign that workflow here; just make Data Files ready to behave as a queue.

Deliverables you should define:

- Requirements amendments for Data Files UX:
    - explicit “ingestion queue” behavior,
    - usage visibility (e.g. “Used by 3 strategies”).
- Any Application additions needed:
    - queries to compute usage (e.g. `IDataUsageService` over strategies and scenarios).
- UI changes to `Data.razor` to:
    - emphasize status and usage,
    - guide users to fix/replace/remove invalid files safely.[^3][^1]

***

### 6. Workflow-level testing additions

Goal: Add test coverage for **end-to-end research journeys** and UI state transitions, not just isolated services.[^1][^3]

Scope and constraints:

- Build on the existing V4 test plan, which already covers domain calculators, migration-safe serialization, DSR, MinBTL, sealed test set flows, and some integration paths.[^3][^1]
- Add tests in three categories:

1. **Workflow integration tests**, e.g.:
        - create strategy from template → save hypothesis → run baseline backtest → launch recommended study → update checklist → unlock final validation.[^1]
2. **UI state tests**, especially:
        - missing descriptors,
        - failed runs,
        - partial studies,
        - empty dashboard/data/study states.[^1]
3. **Sequencing/guard regression tests**, e.g.:
        - trial count increments correctly for single runs and sweeps,
        - sealed test set enforcement works and final validation bypass is respected,
        - user-authored hypothesis is not overwritten on clone/edit.[^5][^3]

Deliverables you should define:

- A small extension to `tasks.md` listing specific workflow integration tests, UI state tests, and sequencing regression tests to add.
- Any test harness or fixture work needed to make these flows practical to test.

***

For each of the six areas above, please:

1. Identify any **Core vs Application vs Web** changes needed (if any).
2. Add/adjust **requirements** in the same style and numbering as the existing specs.
3. Propose a concrete **implementation plan** (checklist-style tasks) similar to the existing `tasks.md` structure, including tests.[^5][^3][^1]
4. Call out any **risks or open questions** that should be resolved before coding.

***

Would you like the prompt tweaked to emphasize any one area (e.g. Strategy Builder, or Dashboard) as higher priority for Kiro to focus on first?

<div align="center">⁂</div>

[^1]: design.md

[^2]: design.md

[^3]: tasks.md

[^4]: requirements.md

[^5]: tasks.md

