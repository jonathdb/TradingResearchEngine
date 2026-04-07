# TradingResearchEngine

An event-driven backtesting engine for quantitative strategy research, built with .NET 8 / C# 12.

## Architecture

```
TradingResearchEngine.sln
src/
  TradingResearchEngine.Core           — domain abstractions, event types, engine, portfolio, metrics
  TradingResearchEngine.Application    — use cases, research workflows, prop-firm module, risk/execution
  TradingResearchEngine.Infrastructure — CSV/HTTP data providers, JSON persistence, reporters
  TradingResearchEngine.Cli            — argument-driven + interactive CLI host
  TradingResearchEngine.Api            — ASP.NET Core minimal API host
  TradingResearchEngine.Web            — Blazor Server UI host (MudBlazor)
  TradingResearchEngine.UnitTests      — xUnit + FsCheck property tests
  TradingResearchEngine.IntegrationTests — end-to-end and infrastructure tests
```

Dependency rule: `Core ← Application ← Infrastructure ← { Cli, Api, Web }`

## Module Boundaries

- **Core Engine**: event hierarchy, heartbeat loop, dispatch table, portfolio, metrics (Sharpe, Sortino, Calmar, RoMaD, equity curve smoothness, average holding period, and more)
- **Research Workflows**: parameter sweep, variance testing, Monte Carlo, walk-forward, parameter perturbation, randomized out-of-sample, scenario comparison, benchmark comparison
- **PropFirmModule**: challenge/instant-funding economics, rule evaluation, variance presets
- **Infrastructure**: CSV, HTTP, and in-memory data providers, JSON file repository, console/markdown reporters

## Built-in Strategies

Strategies are discovered via the `[StrategyName]` registry. Use the name in `ScenarioConfig.StrategyType`.

| Name | Class | Description |
|---|---|---|
| `moving-average-crossover` | `SmaCrossoverStrategy` | Buys on fast/slow SMA crossover, sells on cross-under |
| `breakout` | `BreakoutStrategy` | Buys on N-bar high breakout, sells on N-bar low breakdown |
| `mean-reversion` | `MeanReversionStrategy` | Buys N std devs below SMA, sells on reversion to mean |
| `rsi` | `RsiStrategy` | Buys when RSI drops below oversold, sells above overbought |

## Getting Started

```bash
dotnet build
dotnet test
dotnet run --project src/TradingResearchEngine.Cli -- --scenario path/to/scenario.json
dotnet run --project src/TradingResearchEngine.Web
```

## API Endpoints

| Method | Path | Description |
|---|---|---|
| POST | /scenarios/run | Run a single backtest |
| POST | /scenarios/sweep | Parameter sweep |
| POST | /scenarios/montecarlo | Monte Carlo simulation |
| POST | /scenarios/walkforward | Walk-forward analysis |

## Product Goals

- Reproducible, parameterised backtesting via ScenarioConfig JSON files
- Research workflows as first-class capabilities
- Prop-firm evaluation without modifying core engine
- Clean architecture with enforced layer boundaries
