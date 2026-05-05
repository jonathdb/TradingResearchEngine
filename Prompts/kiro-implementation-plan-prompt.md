# Kiro Prompt: TradingResearchEngine — Full Implementation Plan

## Context

You are working on the **TradingResearchEngine** repository at `https://github.com/jonathdb/TradingResearchEngine`.

This is a .NET 8 / C# 12 backtesting platform built with:
- **Blazor Server** UI using **MudBlazor** components
- **Clean Architecture**: `Core ← Application ← Infrastructure ← Web/Api/Cli`
- **JSON file repositories** for persistence (with a SQLite index layer added in V6)
- **xUnit + FsCheck** for unit and property testing

A comprehensive code review has identified **25 specific improvements** across five phases. Your task is to produce a detailed, file-by-file, spec-driven implementation plan for all of them — ready to execute in Kiro's spec/task workflow.

---

## Instructions for Kiro

For **each improvement item** listed below, produce a Kiro spec containing:

1. **Title** — short imperative sentence
2. **Problem** — what is broken or missing and why it matters
3. **Affected files** — exact file paths in the repo
4. **Acceptance criteria** — numbered, testable "Given / When / Then" or assertion statements
5. **Implementation steps** — ordered list of concrete code changes
6. **Tests to add or update** — specific test file and test case descriptions
7. **Dependencies** — which other items must be completed first (by item number)

Group the specs into the five phases below. Within each phase, order items by dependency (leaf items first).

---

## Phase 1 — Fix Broken Functionality

### Item 1: Fix Fork Handler in Step1ChooseStartingPoint

**Problem**: `Step1ChooseStartingPoint.razor` stores `_forkStrategyId` in local component state but never raises it to the parent `StrategyBuilder.razor` via a callback. Clicking "Fork Existing" and selecting a strategy has zero effect on `BuilderViewModel`.

**Affected files**:
- `src/TradingResearchEngine.Web/Components/Pages/Strategies/Steps/Step1ChooseStartingPoint.razor`
- `src/TradingResearchEngine.Web/Components/Pages/Strategies/StrategyBuilder.razor`
- `src/TradingResearchEngine.Application/Strategy/BuilderViewModel.cs` (if it exists, else the ViewModel class wherever defined)

**Required change**: Add `[Parameter] public EventCallback<StrategyIdentity> OnForkSelected { get; set; }` to Step1. Wire the parent to handle this by loading the forked strategy's `ScenarioConfig` into `BuilderViewModel`, pre-populating all parameters and selecting the correct strategy type.

---

### Item 2: Fix Import JSON — Make It Actually Parse and Apply

**Problem**: `_importJson` in `Step1ChooseStartingPoint.razor` is bound to a text field but never deserialised. The input is silently discarded when the user clicks Next.

**Affected files**:
- `src/TradingResearchEngine.Web/Components/Pages/Strategies/Steps/Step1ChooseStartingPoint.razor`
- `src/TradingResearchEngine.Web/Components/Pages/Strategies/StrategyBuilder.razor`

**Required change**: On form submission in Import mode, deserialise the JSON string into a `ScenarioConfig` using `System.Text.Json`. If valid, raise an `OnImportApplied(ScenarioConfig)` callback to the parent. If invalid, display a `MudAlert` with the parse error. The parent populates `BuilderViewModel` from the config and advances to Step 2.

---

### Item 3: Fix GoToStep — Allow Forward Navigation to Already-Visited Steps

**Problem**: `StrategyBuilder.razor`'s `GoToStep(int step)` method silently ignores any `step >= _vm.CurrentStep`. The builder step indicator appears interactive but is one-directional.

**Affected files**:
- `src/TradingResearchEngine.Web/Components/Pages/Strategies/StrategyBuilder.razor`
- `src/TradingResearchEngine.Web/Components/Shared/BuilderStepIndicator.razor`

**Required change**: Track `_vm.MaxVisitedStep` (the highest step the user has reached). Replace the guard with `if (step <= _vm.MaxVisitedStep)` so users can jump freely to any previously visited step. Update `BuilderStepIndicator` to visually differentiate "current", "visited", and "not yet reached" states.

---

### Item 4: Fix N+1 Queries in ResearchExplorer and StrategyLibrary

**Problem**: Both pages execute one async repository call per row in a `foreach` loop, resulting in O(n) sequential file I/O on every page load.

**Affected files**:
- `src/TradingResearchEngine.Web/Components/Pages/Research/ResearchExplorer.razor`
- `src/TradingResearchEngine.Web/Components/Pages/Strategies/StrategyLibrary.razor`
- `src/TradingResearchEngine.Application/Strategy/IStrategyRepository.cs`
- `src/TradingResearchEngine.Infrastructure/Strategy/StrategyRepository.cs` (or wherever the implementation lives)

**Required change**:
- Add `Task<IReadOnlyDictionary<string, int>> GetVersionCountsAsync(IEnumerable<string> strategyIds)` to `IStrategyRepository`.
- In `ResearchExplorer`, call `StrategyRepo.ListAsync()` once, build a `Dictionary<string, string>` of `versionId → strategyName`, then join in memory — no per-row calls.
- In `StrategyLibrary`, call the new `GetVersionCountsAsync` batch method instead of the loop.

---

### Item 5: Fix IStrategy XML Doc Comment

**Problem**: `IStrategy.cs` still contains "V2 scope: all strategies are long-only. Short-selling is out of scope." Short selling was shipped in V6.

**Affected files**:
- `src/TradingResearchEngine.Core/IStrategy.cs` (or equivalent path)

**Required change**: Update all XML `<summary>` and `<remarks>` blocks to accurately describe the current V6+ capabilities including `Direction.Short` and the full position lifecycle.

---

### Item 6: Delete Orphaned `git` File at Repo Root

**Problem**: A zero-byte file named `git` exists at the repository root, likely an accidental artefact from a shell command.

**Affected files**:
- `/git` (repo root)

**Required change**: Delete the file. Add a `.gitignore` rule `git` at the root level to prevent recurrence.

---

## Phase 2 — Navigation & Discovery

### Item 7: Restructure NavMenu

**Problem**: The NavMenu exposes only a fraction of the application. Seven research-workflow pages are completely unreachable. "Market Data" and "Data Files" are incorrectly grouped under `SETTINGS`.

**Affected files**:
- `src/TradingResearchEngine.Web/Components/Layout/NavMenu.razor`

**Required new NavMenu structure**:
```
DASHBOARD         → /
STRATEGIES
  My Strategies   → /strategies/library
  New Strategy    → /strategies/builder
RESEARCH
  Explorer        → /research/explorer
  Parameter Sweep → /research/sweep
  Monte Carlo     → /research/montecarlo
  Walk-Forward    → /research/walkforward
  Perturbation    → /research/perturbation
  Variance        → /research/variance
  Benchmark       → /research/benchmark
BACKTESTS         → /backtests
PROP FIRM LAB     → /propfirm/evaluate
DATA
  Market Data     → /data/market
  Data Files      → /data
SETTINGS          → /settings
```

Use `MudNavGroup` with `Expanded` state persisted to `localStorage` via JS interop so collapsible groups remember their state across page loads.

---

### Item 8: Add Launch Actions to Research Explorer

**Problem**: `ResearchExplorer.razor` is a read-only log table. There is no way to start a new study from it; users must know the direct URLs.

**Affected files**:
- `src/TradingResearchEngine.Web/Components/Pages/Research/ResearchExplorer.razor`

**Required change**: Add a toolbar above the studies table with a "New Study" button that opens a `MudMenu` (or `MudPopover`) listing all study types: Sweep, Monte Carlo, Walk-Forward, Perturbation, Variance, Benchmark. Each menu item navigates to the corresponding page, optionally pre-seeding the `StrategyId` query param from a selected row. Add a "Filter by Strategy" dropdown that sets the existing `?StrategyId=` query parameter.

---

### Item 9: Add Backtests List Page to NavMenu

**Problem**: Individual backtest result pages (`/backtests/{id}`) are only reachable from the snackbar immediately after a run. There is no way to browse historical runs.

**Affected files**:
- `src/TradingResearchEngine.Web/Components/Pages/Backtests/` (check if a list page exists; create `BacktestList.razor` if not)
- `src/TradingResearchEngine.Web/Components/Layout/NavMenu.razor`

**Required change**: Create (or wire) a `BacktestList.razor` page at `/backtests` that renders a sortable, filterable table of all `BacktestResult` records using the SQLite index repository. Add it to the NavMenu under BACKTESTS.

---

### Item 10: Implement "Stale" Strategy Status

**Problem**: The "Stale" filter in `StrategyLibrary.razor` is hardcoded `Disabled="true"` with "coming soon". No staleness logic exists.

**Affected files**:
- `src/TradingResearchEngine.Web/Components/Pages/Strategies/StrategyLibrary.razor`
- `src/TradingResearchEngine.Application/Strategy/StrategyIdentity.cs`
- `src/TradingResearchEngine.Infrastructure/Configuration/EngineSettings.cs` (or wherever app settings live)

**Required change**:
- Add `StalenessThresholdDays` (default: 30) to engine settings.
- In `StrategyLibrary`, compute `IsStale` as: the strategy has at least one completed run AND the most recent run's date is older than `DateTime.UtcNow - StalenessThreshold`.
- Add `DevelopmentStage.Stale` or handle staleness as a derived status from `GetStatus()`.
- Enable the "Stale" filter chip and make it functional.

---

## Phase 3 — Builder & Engine Quality

### Item 11: Add Schema-Driven Validation to Builder Steps 3 and 4

**Problem**: `CanAdvance()` returns `true` unconditionally for steps 3 and 4. Invalid parameter values pass silently to the runner.

**Affected files**:
- `src/TradingResearchEngine.Web/Components/Pages/Strategies/StrategyBuilder.razor`
- `src/TradingResearchEngine.Web/Components/Pages/Strategies/Steps/Step3Parameters.razor` (or equivalent)
- `src/TradingResearchEngine.Application/Strategy/ParameterMetaAttribute.cs`
- `src/TradingResearchEngine.Application/Strategy/ConfigDraftValidator.cs`

**Required change**:
- For Step 3: iterate the active strategy's `StrategyParameterSchema`. For each parameter with `[ParameterMeta(Min=..., Max=...)]`, validate the current draft value is in range. Display per-field validation messages using MudBlazor form validation. Return `false` from `CanAdvance()` until all fields are valid.
- For Step 4: validate that all risk allocation percentages sum to ≤ 100% and that stop-loss values are positive. Return `false` until valid.

---

### Item 12: Differentiate "Quick Sanity" From "Standard Backtest"

**Problem**: Both launch buttons call `RunUseCase.RunAsync` with the same `ScenarioConfig`. "Quick Sanity" gives no faster feedback than a full run.

**Affected files**:
- `src/TradingResearchEngine.Web/Components/Pages/Strategies/StrategyBuilder.razor`
- `src/TradingResearchEngine.Application/Execution/ScenarioConfig.cs` (or equivalent)

**Required change**: When `OnLaunchAction("quick-sanity")` is called, mutate the config before passing it to the runner:
- Set `DateRange.From = DateTime.UtcNow.AddYears(-2)` (last 2 years only)
- Set `MaxTrials = 1` (single pass, no Monte Carlo)
- Set `ExecutionRealism = ExecutionRealismProfile.NoFriction` (fastest execution)
Show a "Quick Sanity — reduced dataset" badge on the result page when this mode was used.

---

### Item 13: Replace `dynamic` Result Type in HandleRunResult

**Problem**: `HandleRunResult(dynamic result)` in `StrategyBuilder.razor` bypasses compile-time safety and risks `RuntimeBinderException`.

**Affected files**:
- `src/TradingResearchEngine.Web/Components/Pages/Strategies/StrategyBuilder.razor`
- `src/TradingResearchEngine.Application/Execution/RunScenarioUseCase.cs`

**Required change**: Determine the concrete return type of `RunScenarioUseCase.RunAsync` (likely `Result<BacktestResult>` or a discriminated union). Replace the `dynamic` parameter with the concrete type. Add a compile-time check using a `Result` pattern match or `if (result.IsSuccess)` guard.

---

### Item 14: Fix StrategyId Truncation Bug

**Problem**: `$"strategy-{Guid.NewGuid():N}".Substring(0, 20)` produces IDs with only 11 random characters, reducing uniqueness.

**Affected files**:
- `src/TradingResearchEngine.Web/Components/Pages/Strategies/StrategyBuilder.razor` (the `SaveDraftAndVersion` method)

**Required change**: Replace with:
```csharp
var slug = vm.StrategyName
    .ToLowerInvariant()
    .Replace(" ", "-")
    .Replace(Regex.Match(vm.StrategyName, @"[^a-z0-9\-]").Value, "");
strategyId = $"{slug[..Math.Min(slug.Length, 12)]}-{Guid.NewGuid().ToString("N")[..8]}";
```
This produces human-readable, URL-safe IDs like `vol-trend-strat-a1b2c3d4` with 32 bits of randomness. Add a unit test asserting uniqueness across 10,000 rapid generations.

---

### Item 15: Add Equity Curve Chart to Backtest Result Page

**Problem**: The backtest result page shows only tabular metrics. `BacktestResult` contains an equity curve series but it is never rendered visually.

**Affected files**:
- `src/TradingResearchEngine.Web/Components/Pages/Backtests/BacktestDetail.razor` (or equivalent result page)
- `src/TradingResearchEngine.Web/Components/Charts/` (create `EquityCurveChart.razor`)
- `src/TradingResearchEngine.Web/wwwroot/` (add ApexCharts JS interop or use Blazor-ApexCharts NuGet package)

**Required change**:
- Add the `Blazor-ApexCharts` NuGet package (`Blazor-ApexCharts` by `apexcharts`).
- Create `EquityCurveChart.razor` that accepts `IReadOnlyList<(DateTime Date, decimal Equity)>` and renders a line chart with: equity curve (primary y-axis), drawdown shading (secondary y-axis / filled area), benchmark overlay if available.
- Wire into the backtest result page above the metrics table.

---

### Item 16: Extract RobustnessAdvisoryService

**Problem**: Warning threshold logic (`SharpeRatio > 3.0`, `TotalTrades < 30`, etc.) is duplicated verbatim in both `Dashboard.razor` and `StrategyLibrary.razor`.

**Affected files**:
- `src/TradingResearchEngine.Application/Research/RobustnessAdvisoryService.cs` (create)
- `src/TradingResearchEngine.Application/Research/IRobustnessAdvisoryService.cs` (create)
- `src/TradingResearchEngine.Web/Components/Pages/Dashboard.razor`
- `src/TradingResearchEngine.Web/Components/Pages/Strategies/StrategyLibrary.razor`
- `src/TradingResearchEngine.Application/ServiceCollectionExtensions.cs`

**Required change**: Create `IRobustnessAdvisoryService` with `IReadOnlyList<string> GetWarnings(BacktestResult result)`. Register it in DI. Thresholds should be configurable via `IOptions<RobustnessThresholds>` (appsettings). Replace all duplicated warning logic in both Razor pages with a single service call. Add unit tests for each threshold boundary.

---

## Phase 4 — LLM Strategy Assistant

### Item 17: Implement IStrategyIdeaTranslator with Provider Abstraction

**Problem**: No LLM integration exists. New strategies can only be created by choosing a pre-compiled template.

**Affected files** (all new):
- `src/TradingResearchEngine.Application/AI/IStrategyIdeaTranslator.cs`
- `src/TradingResearchEngine.Application/AI/StrategyIdeaResult.cs`
- `src/TradingResearchEngine.Infrastructure/AI/GoogleAiStudioTranslator.cs`
- `src/TradingResearchEngine.Infrastructure/AI/GroqTranslator.cs`
- `src/TradingResearchEngine.Infrastructure/AI/OllamaTranslator.cs`
- `src/TradingResearchEngine.Infrastructure/AI/FallbackStrategyIdeaTranslator.cs`
- `src/TradingResearchEngine.Application/ServiceCollectionExtensions.cs`
- `src/TradingResearchEngine.Web/appsettings.json`
- `Prompts/strategy-idea-translator-system-prompt.md` (new versioned prompt file)

**Required design**:

```csharp
// IStrategyIdeaTranslator.cs
public interface IStrategyIdeaTranslator
{
    Task<StrategyIdeaResult> TranslateAsync(
        string userDescription,
        IReadOnlyList<StrategyTemplate> templates,
        IReadOnlyList<StrategyParameterSchema> schemas,
        CancellationToken ct = default);
}

// StrategyIdeaResult.cs
public record StrategyIdeaResult(
    bool Success,
    string? SelectedTemplateId,
    string? StrategyType,
    Dictionary<string, object>? SuggestedParameters,
    string? SuggestedHypothesis,
    string? SuggestedTimeframe,
    string? FailureReason,
    string? GeneratedStrategyCode = null);
```

**Provider config** (`appsettings.json`):
```json
"LlmProvider": {
  "EnableAIStrategyAssist": true,
  "Provider": "GoogleAIStudio",
  "BaseUrl": "https://generativelanguage.googleapis.com/v1beta/openai/",
  "Model": "gemini-2.5-flash",
  "ApiKey": "",
  "FallbackProviders": ["Groq", "Ollama"]
}
```

**Fallback chain**: GoogleAIStudio → Groq → Ollama. If `EnableAIStrategyAssist` is `false`, return a `StrategyIdeaResult(Success: false, FailureReason: "AI assist is disabled")` immediately without any HTTP calls.

**Prompt file** (`Prompts/strategy-idea-translator-system-prompt.md`): The system prompt content loaded at runtime, so it can be updated without recompiling. Include `{templates_json}` and `{schemas_json}` as interpolation tokens.

**JSON schema enforcement**: For GoogleAIStudio and Groq, set `response_format: { "type": "json_schema", "json_schema": { ... } }` in the request. For Ollama, set `format: "json"` and use grammar-constrained generation if available.

---

### Item 18: Add "Describe Your Idea" Step 0 to Strategy Builder

**Problem**: The builder starts directly at template/fork selection. There is no natural-language entry point.

**Affected files**:
- `src/TradingResearchEngine.Web/Components/Pages/Strategies/Steps/Step0IdeaDescription.razor` (new)
- `src/TradingResearchEngine.Web/Components/Pages/Strategies/StrategyBuilder.razor`
- `src/TradingResearchEngine.Web/Components/Shared/BuilderStepIndicator.razor`

**Required UX**:
- A `MudTextField` with placeholder "Describe your trading idea in plain language..."  
- A "Use AI to prefill →" `MudButton` (disabled when `IStrategyIdeaTranslator` returns `Success: false` due to feature flag)
- A "Skip, start manually →" link that advances to Step 1 without calling the LLM
- While the LLM call is in-flight, show a `MudProgressLinear` with the text "Analysing your idea…"
- On success: advance to Step 2 with all fields pre-populated; show a `MudAlert` "AI pre-filled your strategy — review and adjust below" with a dismiss button
- On failure: show the error and stay on Step 0 with the option to retry or skip

**Dependency**: Item 17 must be complete.

---

### Item 19: Add Core.Indicators Library

**Problem**: All six strategies implement rolling-window indicator arithmetic inline. There is no shared, composable indicator layer, which also blocks Level 2 code generation.

**Affected files** (all new under `src/TradingResearchEngine.Core/Indicators/`):
- `IIndicator.cs`
- `SimpleMovingAverage.cs`
- `ExponentialMovingAverage.cs`
- `AverageTrueRange.cs`
- `RelativeStrengthIndex.cs`
- `BollingerBands.cs`
- `RollingZScore.cs`
- `DonchianChannel.cs`

**Required design**: Each indicator is a stateful object with:
```csharp
public interface IIndicator<TOutput>
{
    TOutput? Value { get; }       // null until warmed up
    bool IsReady { get; }
    void Update(decimal close, decimal? high = null, decimal? low = null);
    void Reset();
}
```

Migrate `ZScoreMeanReversionStrategy` to use `RollingZScore` as a proof-of-concept refactor. Add unit tests using deterministic price series for each indicator verifying values against a known-correct reference (e.g., Excel-calculated).

---

### Item 20: Implement Dynamic Roslyn Strategy Compiler

**Problem**: There is no mechanism to register a new strategy type at runtime without recompiling the application.

**Affected files** (all new):
- `src/TradingResearchEngine.Application/Strategies/DynamicStrategyRegistry.cs`
- `src/TradingResearchEngine.Infrastructure/Compilation/RoslynStrategyCompiler.cs`
- `src/TradingResearchEngine.Infrastructure/Compilation/IRoslynStrategyCompiler.cs`
- `src/TradingResearchEngine.Infrastructure/Compilation/StrategyCodeSanitiser.cs`
- `src/TradingResearchEngine.Application/Strategy/StrategyRegistry.cs` (extend to include dynamic entries)

**Security requirements** (mandatory):
- `StrategyCodeSanitiser` must reject code containing any of: `HttpClient`, `WebClient`, `File.`, `Directory.`, `Process.`, `Environment.`, `Assembly.Load`, `Type.GetType`, `Activator.CreateInstance`, `Marshal`, `unsafe`, `extern`, `DllImport`.
- Compile in a restricted `AssemblyLoadContext` that does not grant file system or network access.
- The generated type must implement `IStrategy` — reject compilation if it does not.
- Log the full source code, sanitiser result, and compilation diagnostics to a structured audit log.

**Only activate this feature when `EnableDynamicStrategyCompilation: true` is set in appsettings.** Dependency: Item 19.

---

### Item 21: Add Prompt Management — Load System Prompts from Files

**Problem**: If LLM system prompts are hardcoded strings in C# files, updating them requires recompilation.

**Affected files**:
- `src/TradingResearchEngine.Infrastructure/AI/PromptLoader.cs` (new)
- `Prompts/strategy-idea-translator-system-prompt.md` (created in Item 17)
- `Prompts/strategy-code-generator-system-prompt.md` (new, for Level 2)

**Required change**: `PromptLoader` reads prompt files from the `Prompts/` directory at startup, caches them in memory, and exposes them via `IPromptLoader.GetPrompt(string promptName)`. The LLM providers call this to get the current system prompt text. Hot-reload is optional but desirable (use `IFileProvider` watch).

---

## Phase 5 — Long-Term Architecture

### Item 22: Switch Web Pages to SQLite Index for BacktestResult Queries

**Problem**: `Dashboard.razor` and `StrategyLibrary.razor` call `ResultRepo.ListAsync()` which deserialises the entire result collection on every load.

**Affected files**:
- `src/TradingResearchEngine.Web/Components/Pages/Dashboard.razor`
- `src/TradingResearchEngine.Web/Components/Pages/Strategies/StrategyLibrary.razor`
- `src/TradingResearchEngine.Infrastructure/Persistence/SqliteIndexRepository.cs`
- `src/TradingResearchEngine.Application/ServiceCollectionExtensions.cs`

**Required change**: Inject `ISqliteBacktestIndex` (or equivalent) alongside the existing repository. Replace `ListAsync()` calls with filtered index queries:
- Dashboard: `GetRecentRunsAsync(limit: 10)` and `GetLastRunPerStrategyAsync()`
- StrategyLibrary: `GetRunSummariesByStrategyAsync(strategyId)`

---

### Item 23: Add CancellationToken to All Blazor Page Initialisation

**Problem**: Multiple pages perform async I/O in `OnInitializedAsync` without honouring component disposal, risking use-after-dispose exceptions.

**Affected files**:
- `src/TradingResearchEngine.Web/Components/Pages/Dashboard.razor`
- `src/TradingResearchEngine.Web/Components/Pages/Research/ResearchExplorer.razor`
- `src/TradingResearchEngine.Web/Components/Pages/Strategies/StrategyLibrary.razor`
- (any other Razor component with async OnInitializedAsync and no CancellationToken)

**Required change**: For each affected component, add:
```csharp
private readonly CancellationTokenSource _cts = new();
public void Dispose() => _cts.Cancel();
```
Pass `_cts.Token` to all async repository calls. Implement `IDisposable` on each component.

---

### Item 24: Build Core.Indicators as Foundation for Visual Composer

**Note**: This item extends Item 19. Once the indicator library exists, expose indicator metadata (name, parameters, output type) in a format that a future drag-and-drop UI can use.

**Affected files**:
- `src/TradingResearchEngine.Core/Indicators/IndicatorRegistry.cs` (new)
- `src/TradingResearchEngine.Core/Indicators/IndicatorDescriptor.cs` (new)

**Required design**: `IndicatorRegistry.All` returns `IReadOnlyList<IndicatorDescriptor>` — parallel to `DefaultStrategyTemplates.All`. Each descriptor has: `Name`, `Description`, `Parameters` (with type, min, max, default), `OutputType`. This is the data contract for the future signal composer UI.

**Dependency**: Item 19.

---

### Item 25: Add Signal Composer UI (Visual Canvas)

**Problem**: The only way to create strategy logic is through pre-compiled C# or LLM code generation. A visual canvas approach would serve non-developers.

**Affected files** (all new):
- `src/TradingResearchEngine.Web/Components/Pages/Strategies/SignalComposer.razor`
- `src/TradingResearchEngine.Web/Components/Composer/IndicatorNode.razor`
- `src/TradingResearchEngine.Web/Components/Composer/RuleNode.razor`
- `src/TradingResearchEngine.Web/Components/Composer/ComposerCanvas.razor`
- `src/TradingResearchEngine.Application/AI/ComposerToStrategyTranslator.cs` (converts composer graph → IStrategy)

**Required design**: A node-based canvas (using a Blazor port of a flow-graph library, or a custom SVG canvas) where:
- **Indicator nodes** come from `IndicatorRegistry.All` (Item 24)
- **Rule nodes** represent conditions: `CrossAbove`, `CrossBelow`, `GreaterThan`, `LessThan`, `And`, `Or`
- **Action nodes**: `Enter Long`, `Enter Short`, `Exit`, `Size by ATR`
- The graph is serialised to a `ComposerGraph` JSON model
- `ComposerToStrategyTranslator` compiles the graph into a dynamic `IStrategy` using Roslyn (Item 20) or emits it as named-template config

**Dependencies**: Items 19, 20, 24.

---

## Output Format for Kiro

For each of the 25 items above, produce a spec in this format:

```markdown
## Spec: [Item Number] — [Title]

### Problem Statement
[2–3 sentences describing the bug or gap]

### Files to Change
- `path/to/file.cs` — [what changes]
- `path/to/file.razor` — [what changes]

### Acceptance Criteria
1. Given [...], when [...], then [...]
2. ...

### Implementation Steps
1. ...
2. ...

### Tests
- `UnitTests/[TestFile].cs`: add test `[TestName]` asserting [...]

### Blocked By
- Item N (if applicable)
```

After all 25 specs, produce a **Dependency Graph** showing which items must precede which, and a **Sprint Allocation** suggesting which items can run in parallel within each phase.
