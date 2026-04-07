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

## Variance Presets

`PropFirmVarianceWorkflow` applies Conservative, Base, and Strong presets (plus an optional user-defined preset) to an `InstantFundingConfig` and returns a `PropFirmVarianceResult` containing economics for each variant. It is registered as a scoped service in DI.

| Preset | GrossReturn | Friction | PassRate |
|---|---|---|---|
| Conservative | base × 0.70 | base × 0.85 | base × 0.80 |
| Base | unchanged | unchanged | unchanged |
| Strong | base × 1.30 | base × 1.10 | base × 1.15 |
| UserDefined | from config | from config | from config |
