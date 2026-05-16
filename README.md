# TradingResearchEngine

A quantitative strategy research platform with an interactive Web UI for backtesting, parameter optimization, and prop-firm evaluation. Built with .NET 8, Blazor Server, and MudBlazor.

## Getting Started

```bash
# Build the solution
dotnet build

# Run the test suite
dotnet test

# Launch the application
dotnet run --project src/TradingResearchEngine.Web
```

After launch, open your browser to **http://localhost:5260**.

The **Dashboard** is the landing page. On first launch you will see KPI tiles (Total Strategies, Last Sharpe, Total Runs, Active Studies), a recent runs table, robustness warnings for completed backtests, and navigation cards linking to the Strategy Library, Research Explorer, and Data Files sections. All tiles start at zero until you create your first strategy and run a backtest.

## Features

- **Strategy Library** — Create, version, and manage trading strategies with development stage tracking
- **Backtesting Engine** — Event-driven replay with configurable execution realism profiles
- **Parameter Sweep** — Schema-driven grid search with range inputs and auto-selection
- **Research Workflows** — Monte Carlo, walk-forward, variance testing, sensitivity analysis, CPCV
- **Prop Firm Evaluation** — Multi-phase challenge simulation with pre-built rule packs (FTMO, TopStep, MyFundedFX, The5ers)
- **AI Strategy Builder** — Natural-language strategy generation via Google Gemini
- **Interactive Charts** — Equity curves, drawdown, monthly returns, trade PnL histograms (Plotly.Blazor)
- **Multi-Format Export** — Markdown reports, CSV trade logs, JSON results, MQL4/MQL5/PineScript code export

## Environment Setup

### Google Gemini API Key (AI Strategy Assistant)

The AI Strategy Assistant requires a Google Gemini API key:

**PowerShell (current session):**
```powershell
$env:Gemini__ApiKey = "your-api-key-here"
```

**Or via `appsettings.json` (local development only — do not commit):**
```json
{
  "Gemini": {
    "ApiKey": "your-api-key-here",
    "ModelName": "gemini-2.5-flash",
    "CallTimeout": "00:01:00"
  }
}
```

| Setting | Default | Description |
|---|---|---|
| `ApiKey` | — | Google Gemini API key (required for AI features) |
| `ModelName` | `gemini-2.5-flash` | Gemini model identifier |
| `MaxRetries` | 5 | Retry attempts for transient failures (6 total including initial) |
| `CallTimeout` | `00:01:00` (60s) | Maximum time per outbound AI API call before cancellation |
| `MaxPromptLength` | 30000 | Max combined system prompt + user message characters |
| `CircuitBreakerThreshold` | 3 | Consecutive 429 failures before circuit opens |
| `CircuitBreakerDurationSeconds` | 60 | Seconds the circuit breaker stays open |
| `BaseRetryDelaySeconds` | 2 | Base delay for exponential backoff between retries |

If the key is not set, AI assistant features are disabled gracefully without crashing the application.

## Architecture

```
TradingResearchEngine.sln
src/
  TradingResearchEngine.Core            — domain abstractions, event types, engine, portfolio, metrics
  TradingResearchEngine.Application     — use cases, research workflows, prop-firm module, risk/execution
  TradingResearchEngine.Infrastructure  — CSV/HTTP data providers, JSON/SQLite persistence, reporters
  TradingResearchEngine.Web             — Blazor Server UI host (sole application entry point)
  TradingResearchEngine.Benchmarks      — BenchmarkDotNet performance suite
  TradingResearchEngine.UnitTests       — xUnit + FsCheck property tests
  TradingResearchEngine.IntegrationTests — end-to-end and infrastructure tests
```

**Dependency rule:** `Core ← Application ← Infrastructure ← Web`

## Built-in Strategies

Strategies are discovered via the `[StrategyName]` registry:

| Name | Description |
|------|-------------|
| `volatility-scaled-trend` | Trend following with fast/slow SMA crossover gated by ATR warmup |
| `zscore-mean-reversion` | Bidirectional z-score mean reversion with threshold entry/exit |
| `donchian-breakout` | Long-only Donchian Channel breakout with lagged bands |
| `stationary-mean-reversion` | Z-score mean reversion with ADF stationarity filter |
| `macro-regime-rotation` | Multi-regime rotation using volatility, trend, and momentum indicators |
| `baseline-buy-and-hold` | Passive buy-and-hold benchmark for strategy comparison |

## Documentation

| File | Description |
|------|-------------|
| [docs/BacktestingEngineImplementationNotes.md](docs/BacktestingEngineImplementationNotes.md) | Technical implementation details of the backtesting engine |
| [docs/BacktestingEngineOriginalNotes.md](docs/BacktestingEngineOriginalNotes.md) | Original design notes and rationale for the engine architecture |
| [docs/EventDrivenArchitectureNotes.md](docs/EventDrivenArchitectureNotes.md) | Event-driven architecture patterns and dispatch mechanics |
| [docs/PropFirmSuiteReference.md](docs/PropFirmSuiteReference.md) | Prop firm evaluation module reference and rule pack documentation |
| [docs/UI-Planning-Specification.md](docs/UI-Planning-Specification.md) | Web UI planning specification and component design |
| [docs/V5-Developer-Guide.md](docs/V5-Developer-Guide.md) | Developer guide for V5 config decomposition and parameter schemas |
| [docs/V5-Migration-Guide.md](docs/V5-Migration-Guide.md) | Migration guide for upgrading to V5 ScenarioConfig structure |
| [docs/V5-Quant-Assumptions.md](docs/V5-Quant-Assumptions.md) | Quantitative assumptions and annualisation conventions |

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for version history and release notes.
