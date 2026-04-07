# Testing Standards

## Frameworks

- xUnit for all example-based tests
- FsCheck.Xunit for all property-based tests
- Moq for mocking in UnitTests only

## Project Boundaries

- UnitTests references Core and Application only — never Infrastructure, Cli, or Api.
- IntegrationTests may reference all projects.
- All external dependencies in UnitTests are replaced with in-memory fakes or Moq mocks.

## Unit Tests

Cover all Core domain logic and all Application use cases:

- `MetricsCalculator` — flat/rising/peak-to-trough curves; null metrics when zero trades
- `Portfolio` — buy/sell cash flow; margin breach clamp; RealisedPnl; EquityCurve append
- `BacktestEngine` dispatch — unrecognised event discarded; strategy exception → Status=Failed; MalformedRecordCount
- `DefaultRiskLayer` — RiskRejection on zero quantity; MaxExposurePercent enforcement
- `SimulatedExecutionHandler` — slippage and commission applied to fill price
- Slippage/commission models — zero models return zero; fixed models return configured values
- `RunScenarioUseCase` — validation error on bad config; successful run returns Status=Completed
- Research workflows — empty grid fallback; SimulationCount < 1 error; short data error; < 2 results error
- `PropFirmEvaluator` — ChallengeProbability formula; BreakevenMonths=null; rule violations; USD-only

## Property-Based Tests

All property tests live in UnitTests. Each is tagged with:

```csharp
// Feature: trading-research-engine, Property N: <description>
```

Minimum 100 iterations per property (`[Property(MaxTest = 100)]`).

The eight required properties are:

1. BacktestResult JSON round-trip (Requirements 10.6, 27.3)
2. EquityCurve length equals Fill count (Requirements 8.6, 27.4)
3. Cash conservation (Requirement 8.4)
4. RiskLayer mandatory — every FillEvent traces to a RiskLayer-approved OrderEvent (Requirements 6.1, 6.5)
5. Deterministic replay — same seed + data → identical BacktestResult (Requirements 3.5, 11.5)
6. Monte Carlo seed reproducibility (Requirement 14.5)
7. WalkForward window count formula (Requirement 15.3)
8. BreakevenMonths formula (Requirement 19.5)

## Integration Tests

- `CsvDataProvider` fixture test — fixture CSV in `src/TradingResearchEngine.IntegrationTests/fixtures/`
- Full end-to-end engine test with CSV data and a simple moving-average strategy
- API endpoint tests via `WebApplicationFactory`
- `JsonFileRepository<T>` CRUD against a temp directory

## Naming Convention

- Test classes: `<SubjectUnderTest>Tests`
- Test methods: `<MethodOrScenario>_<Condition>_<ExpectedOutcome>`
- Property test classes: `<SubjectUnderTest>Properties`
