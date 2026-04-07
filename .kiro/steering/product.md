# Product Goals and Module Boundaries

## Purpose

TradingResearchEngine is an event-driven backtesting engine for quantitative strategy research.
Its primary output is a structured `BacktestResult` that feeds into research workflows and a prop-firm evaluation suite.

## V1 Scope

- Bar-level and tick-level replay via a heartbeat loop
- Pluggable strategy, risk, slippage, and commission components
- Research workflows: parameter sweep, variance testing, Monte Carlo, walk-forward
- Prop-firm challenge and instant-funding economics modelling
- CLI host (argument-driven + interactive) and ASP.NET Core minimal API host
- CSV and HTTP REST data providers
- JSON file persistence
- Console and Markdown reporting

## Module Boundaries

### Core Engine
Owns the event hierarchy, EventQueue, heartbeat loop, dispatch table, Portfolio, and MetricsCalculator.
No business logic from Application or Infrastructure may leak into Core.

### Research Workflows
First-class Application-layer orchestrations. They consume `BacktestResult` and run engine instances.
They do not modify Core abstractions.

### PropFirmModule
A bounded Application-layer module. It consumes `BacktestResult` and research outputs.
It does not extend or modify Core engine abstractions.
It operates in USD only.

### Infrastructure
Concrete implementations of Core/Application interfaces only.
No domain logic. No direct references to engine internals beyond the interfaces defined in Core.

### Hosts (Cli, Api)
Composition roots only. Wire DI, parse input, invoke use cases, render output.
No business logic in hosts.

## Out of Scope for V1

- Live trading or paper trading
- Database persistence (designed for substitution; V1 is JSON files)
- Named third-party data provider integrations (Alpaca, Polygon) beyond the HTTP stub
- Multi-currency portfolio tracking
