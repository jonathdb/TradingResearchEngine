# Kiro Spec: Implement Donchian Breakout Strategy & Fix Zero-Trades Backtest

## Context

This is the TradingResearchEngine — an event-driven backtesting framework in C#.
The engine is built around `IStrategy` / `BacktestEngine` / `StrategyRegistry`.

The problem: running a backtest with `StrategyType: "donchian-breakout"` produces
0 trades because:
1. No concrete `IStrategy` implementation for `donchian-breakout` exists anywhere in the codebase
2. `StrategyRegistry.RegisterAssembly()` is never called at startup, so the registry is always empty
3. The `DefaultStrategyTemplates` entry for `tpl-donchian-breakout` lists `RecommendedTimeframe: "Daily"` which may bleed into ScenarioConfig causing interval mismatch against 15M CSV data

---

## Requirements

### 1. Create `DonchianBreakoutStrategy`

**File:** `src/TradingResearchEngine.Infrastructure/Strategies/DonchianBreakoutStrategy.cs`

Implement `IStrategy` from `TradingResearchEngine.Core.Strategy`.

**Logic:**
- Constructor accepts `int period = 20`
- Decorated with `[StrategyName("donchian-breakout")]`
- On each `MarketDataEvent` (expect `BarEvent`):
  - Maintain a rolling window of the last `period` bars' highs and lows (use a circular buffer or queue — do NOT use LINQ on every bar)
  - During warmup (fewer than `period` bars seen), return empty list
  - After warmup:
    - Compute `channelHigh` = max of the previous `period` bars' highs (use bars[0..period-1], i.e. EXCLUDE the current bar to avoid look-ahead bias)
    - Compute `channelLow` = min of the previous `period` bars' lows (same exclusion)
    - **Entry signal:** If no position is currently held AND current bar's `Close > channelHigh`, emit a `SignalEvent` with `Direction.Long`
    - **Exit signal:** If a position IS currently held AND current bar's `Close < channelLow`, emit a `SignalEvent` with `Direction.Flat`
  - Track position state internally via a `bool _inPosition` field; update it when signals are emitted
- Return `IReadOnlyList<EngineEvent>` from `OnMarketData`

**Constraints:**
- Long-only (per the V2 scope comment in `IStrategy.cs`)
- No LINQ inside the hot path per-bar loop
- No external dependencies beyond `TradingResearchEngine.Core`

---

### 2. Create Unit Tests

**File:** `src/TradingResearchEngine.UnitTests/Strategies/DonchianBreakoutStrategyTests.cs`

Write xUnit tests covering:
- Returns empty list during warmup (fewer than `period` bars)
- Emits `Direction.Long` signal on first bar that closes above channel high
- Does NOT emit a second Long signal while already in position
- Emits `Direction.Flat` signal when close drops below channel low while in position
- Uses correct previous-bar channel (not current bar) — verify look-ahead exclusion
- Works correctly with `period = 1` edge case

Use `BarEvent` records directly; do not mock the event type.

---

### 3. Register the Strategy Assembly at Startup

**File:** `src/TradingResearchEngine.Infrastructure/ServiceCollectionExtensions.cs`

In `AddTradingResearchEngineInfrastructure()`, after existing service registrations, add:

```csharp
// Register all IStrategy implementations from this assembly
services.AddSingleton(sp =>
{
    var registry = new TradingResearchEngine.Application.Strategy.StrategyRegistry();
    registry.RegisterAssembly(typeof(DonchianBreakoutStrategy).Assembly);
    return registry;
});
```

Also add `StrategyRegistry` to the `using` imports at the top if needed.
The `StrategyRegistry` type is in `TradingResearchEngine.Application.Strategy`.

---

### 4. Fix the `DefaultStrategyTemplates` Timeframe

**File:** `src/TradingResearchEngine.Application/Strategy/StrategyTemplate.cs`

In the `tpl-donchian-breakout` entry inside `DefaultStrategyTemplates.All`, change:

```csharp
"Daily",   // RecommendedTimeframe — currently hardcoded
```

to:

```csharp
"Any",   // RecommendedTimeframe — strategy is timeframe-agnostic
```

This is a metadata-only field and does not affect engine execution, but prevents it being accidentally copied into `DataProviderOptions["Interval"]` in UI-driven scenario creation.

---

### 5. Add a `ScenarioConfig` Validation Warning for Interval Mismatch

**File:** `src/TradingResearchEngine.Application/Strategy/ConfigDraftValidator.cs`
(or wherever scenario validation lives — check the existing validator)

Add a warning (not a hard error) if:
- `DataProviderOptions["Interval"]` is present AND equals `"Daily"` AND `Timeframe` is not null and not `"Daily"` or `"Any"`

Return the warning as part of the validation result so the UI can surface it.

---

## Acceptance Criteria

- [ ] `DonchianBreakoutStrategy` builds with no warnings
- [ ] All unit tests pass
- [ ] Running the EURUSD 15M backtest with `StrategyType: "donchian-breakout"` and `period: 20` over at least 1 year of data produces > 50 trades
- [ ] `StrategyRegistry.KnownNames` includes `"donchian-breakout"` after startup
- [ ] No look-ahead bias: channel is computed from bars BEFORE the current bar

---

## Files to Create / Modify

| File | Action |
|------|--------|
| `src/TradingResearchEngine.Infrastructure/Strategies/DonchianBreakoutStrategy.cs` | CREATE |
| `src/TradingResearchEngine.UnitTests/Strategies/DonchianBreakoutStrategyTests.cs` | CREATE |
| `src/TradingResearchEngine.Infrastructure/ServiceCollectionExtensions.cs` | MODIFY — add registry registration |
| `src/TradingResearchEngine.Application/Strategy/StrategyTemplate.cs` | MODIFY — fix RecommendedTimeframe |
| `src/TradingResearchEngine.Application/Strategy/ConfigDraftValidator.cs` | MODIFY — add interval mismatch warning |

---

## Key Types Reference

```
TradingResearchEngine.Core.Strategy.IStrategy
TradingResearchEngine.Core.Strategy.StrategyNameAttribute
TradingResearchEngine.Core.Events.BarEvent        (Symbol, Interval, Open, High, Low, Close, Volume, Timestamp)
TradingResearchEngine.Core.Events.SignalEvent     (Symbol, Direction, Strength, Timestamp)
TradingResearchEngine.Core.Events.EngineEvent     (base type)
TradingResearchEngine.Core.Events.Direction       (Long, Flat)
TradingResearchEngine.Application.Strategy.StrategyRegistry
```
