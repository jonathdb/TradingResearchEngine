# Prop-Firm Evaluation Suite Reference

## Module Boundary

The PropFirmModule is a bounded context within the Application layer. It consumes `BacktestResult` and research workflow outputs but does not modify Core engine abstractions. USD-only.

## Economics Formulas

### Challenge Probability

```
ChallengeProbability = PassRatePercent × PassToFundedConversionPercent / 10000
```

Both inputs are percentages (e.g. 80 and 50 → 0.4).

### Monthly Payout Expectancy

```
MonthlyPayout = NotionalSizeUsd × (GrossMonthlyReturnPercent / 100)
              × (PayoutSplitPercent / 100) × PayoutFrictionFactor
```

### Lifetime Expected Value

```
LifetimeEV = (MonthlyPayout × ExpectedPayoutMonths) − AccountFeeUsd
```

### Breakeven Months

```
BreakevenMonths = ceil(AccountFeeUsd / MonthlyPayout)
```

When `MonthlyPayout ≤ 0`, `BreakevenMonths` is `null` and a warning is logged.

## Rule Evaluation

`PropFirmEvaluator.Evaluate(BacktestResult, FirmRuleSet)` checks:

1. Max total drawdown — `result.MaxDrawdown > rules.MaxTotalDrawdownPercent / 100`
2. Minimum trading days — `result.TotalTrades < rules.MinTradingDays`
3. Consistency rule — no single trade exceeds X% of total profit

Any violation marks `ChallengeOutcome = Failed`.

## FirmRuleSet Persistence

`FirmRuleSet` implements `IHasId` (mapping `Id` to `FirmName`), so it can be persisted via `IRepository<FirmRuleSet>` using the same `JsonFileRepository<T>` infrastructure as `BacktestResult` and `ScenarioConfig`. This enables save/load/delete of firm rule sets for reuse across evaluations.

## Variance Presets

`PropFirmVarianceWorkflow` applies Conservative, Base, and Strong presets (plus an optional user-defined preset) to an `InstantFundingConfig` and returns a `PropFirmVarianceResult` containing economics for each variant. It is registered as a scoped service in DI.

| Preset | GrossReturn | Friction | PassRate |
|---|---|---|---|
| Conservative | base × 0.70 | base × 0.85 | base × 0.80 |
| Base | unchanged | unchanged | unchanged |
| Strong | base × 1.30 | base × 1.10 | base × 1.15 |
| UserDefined | from config | from config | from config |

## V3 Enriched Prop Firm Model

V3 introduces a richer prop firm model alongside the existing `FirmRuleSet`. The new types live in `Application/PropFirm/`.

### PropFirmRulePack

Replaces/extends `FirmRuleSet` for firms with multi-phase challenges. Implements `IHasId` via `RulePackId`.

| Field | Type | Description |
|---|---|---|
| RulePackId | string | Unique identifier (e.g. `ftmo-100k-phase1`) |
| FirmName | string | Firm name (e.g. `FTMO`) |
| ChallengeName | string | Challenge description (e.g. `100k Challenge Phase 1`) |
| AccountSizeUsd | decimal | Account size in USD |
| Phases | IReadOnlyList\<ChallengePhase\> | One or more challenge phases |
| PayoutSplitPercent | decimal? | Payout split percentage |
| ScalingThresholdPercent | decimal? | Scaling plan threshold |
| UnsupportedRules | IReadOnlyList\<string\>? | Rules that cannot be modelled |
| IsBuiltIn | bool | Whether this is a shipped rule pack |
| Notes | string? | Free-text notes |

### ChallengePhase

A single phase in a prop firm challenge (e.g. Phase 1, Phase 2, Funded).

| Field | Type | Description |
|---|---|---|
| PhaseName | string | Phase identifier (e.g. `Phase 1`) |
| ProfitTargetPercent | decimal | Required profit target as percentage |
| MaxDailyDrawdownPercent | decimal | Maximum daily drawdown limit |
| MaxTotalDrawdownPercent | decimal | Maximum total drawdown limit |
| MinTradingDays | int | Minimum required trading days |
| MaxTradingDays | int? | Maximum allowed trading days (null = unlimited) |
| ConsistencyRulePercent | decimal? | Max single-trade profit as % of total (null = no rule) |
| TrailingDrawdown | bool | Whether drawdown is trailing vs static |

### PhaseEvaluationResult

Per-phase evaluation output. Fields: `PhaseName`, `Passed` (bool), `Rules` (list of `RuleResult`).

### RuleResult

Per-rule evaluation output. Fields: `RuleName`, `Status` (`RuleStatus`), `ActualValue`, `LimitValue`, `Margin` (positive = within limit, negative = breached).

### RuleStatus Enum

| Value | Meaning |
|---|---|
| `Passed` | Rule passed with comfortable margin |
| `NearBreach` | Rule passed but within 20% of the limit |
| `Failed` | Rule violated |
