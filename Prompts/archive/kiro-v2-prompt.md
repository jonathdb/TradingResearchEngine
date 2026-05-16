# Kiro Prompt: TradingResearchEngine V2 — Engine Correctness Overhaul

## Context

This is the V2 specification prompt for TradingResearchEngine. V1 shipped a working
event-driven backtesting engine with a correct pipeline topology, solid domain boundaries,
and a useful set of reference strategies. An expert quant and statistical engineering review
has identified a set of critical correctness bugs and medium-priority improvements that make
the current backtest results untrustworthy. V2 addresses all of these before any UI work is
considered (UI rework is explicitly V3).

The existing `.kiro/steering/` documents, `.kiro/specs/trading-research-engine/requirements.md`,
`design.md`, and `tasks.md` must all be updated to reflect the V2 changes described below.
Do not discard existing requirements — amend and extend them. Add a `## V2` section to
`product.md` under the existing `## V1 Scope` section.

---

## Critical Bugs to Fix (All results are currently untrustworthy until these are resolved)

### BUG-01 — Look-Ahead Bias in BacktestEngine.Dispatch()

**File:** `src/TradingResearchEngine.Application/Engine/BacktestEngine.cs`

**Problem:**  
When a `MarketDataEvent` arrives, the strategy immediately produces a `SignalEvent`, the
`RiskLayer` immediately produces an approved `OrderEvent`, and `SimulatedExecutionHandler`
immediately executes a `FillEvent` — all using the same bar's data, including its `Close`
price. This means the strategy observes the completed close and fills at that same close,
which is impossible in live trading. Every strategy is currently filled at the exact price
it observed to make its decision. This is classic look-ahead bias.

**Required fix:**  
Introduce a pending-order queue in the engine's run state. When an `OrderEvent` is approved
by the `RiskLayer`, it must be placed into the pending queue rather than dispatched
immediately to the execution handler. At the start of processing each new `MarketDataEvent`,
all pending orders from the previous bar are filled first using the new bar's data (use
`bar.Open` as the base fill price for `Market` orders). Only after pending fills are
dispatched does the new bar get passed to the strategy.

The correct event processing order per bar must be:
1. Fill all orders pending from the previous bar using the new bar's Open price
2. Update portfolio mark-to-market with the new bar's Close (see BUG-03)
3. Pass the new bar to the strategy
4. Any new signals → risk layer → approved orders go into the pending queue for next bar

Add a property `FillMode` (enum: `NextBarOpen`, `SameBarClose`) to `ScenarioConfig` with
`NextBarOpen` as the default, so legacy test cases can be preserved with `SameBarClose` if
needed, but the default behaviour is now correct.

**Impact:** All current backtest PnL figures are overstated. This is the single most
important fix in V2.

---

### BUG-02 — Sharpe and Sortino Ratios are Statistically Wrong

**File:** `src/TradingResearchEngine.Core/MetricsCalculator.cs`

**Problem:**  
`ComputeSharpeRatio` and `ComputeSortinoRatio` compute their return series from
`ClosedTrade.NetPnl` (absolute currency values), then annualise with a hardcoded
`Math.Sqrt(252)` factor. This conflates two errors:

1. Trade-frequency returns mixed with a daily annualisation constant. If a strategy trades
   3 times per year, the annualisation blows up the ratio by ~9x. If it trades 10 times per
   day, it understates it.
2. Absolute currency returns are not normalised to position size. A $500 profit on a
   $5,000 position and a $500 profit on a $50,000 position are economically different but
   treated identically.

**Required fix:**  
Both ratios must be computed from the **time-series equity curve** (period-by-period returns
derived from `EquityCurvePoint` records), not from the trade list. The annualisation factor
must be derived from the actual bar interval in `ScenarioConfig` (e.g., 252 for daily bars,
252*6.5 for 1-hour bars on equities, 365*24 for crypto hourly, etc.). Add a
`BarsPerYear: int` field to `ScenarioConfig` (or derive it from `Interval`) and pass it
through to `MetricsCalculator`.

For Sortino, the downside deviation must also use period returns, not trade PnL.

The existing `GetNetReturns(IReadOnlyList<ClosedTrade>)` helper must be replaced or removed.

---

### BUG-03 — Portfolio Mark-to-Market Only Updates on Fills

**File:** `src/TradingResearchEngine.Core/Portfolio.cs`

**Problem:**  
`MarkToMarketFromFill` is called only when a fill arrives, so `TotalEquity` (and the equity
curve) is only updated at trade events. Between trades, `TotalEquity` is stale. For
strategies with infrequent trades, the reported drawdown is severely understated because the
equity curve has no data points between entries and exits.

**Required fix:**  
Add a `MarkToMarket(string symbol, decimal price, DateTimeOffset timestamp)` method to
`Portfolio` that updates the unrealised P&L for an open position and appends an
`EquityCurvePoint`. This method must be called by the engine on every `BarEvent` (or
`TickEvent`), after pending fills are processed and before the strategy is invoked. The
engine loop (BUG-01 fix) must call this as step 2 of per-bar processing.

---

### BUG-04 — Direction.Short Causes Silent Portfolio Accounting Error

**File:** `src/TradingResearchEngine.Core/Portfolio.cs`

**Problem:**  
When a `FillEvent` with `Direction.Short` arrives and no matching long position exists
(i.e., the strategy intends to open a fresh short, not close a long), the current code adds
the fill proceeds to `CashBalance` without recording any short position liability. Cash
inflates silently and no short position is tracked.

All current concrete strategies are long-only (they emit `Direction.Long` or
`Direction.Flat`). The engine's `Direction` enum therefore conflates "open short" with
"close long" semantics.

**Required fix — choose one of the following and document the decision:**  
Option A (Recommended for long-only scope): Rename `Direction.Short` to
`Direction.Flat` wherever it is used as an exit signal. Remove the `Short` enum value.
Add an XML doc comment to `Direction` and `IStrategy` stating that all strategies are
long-only in V2; short-selling is out of scope.

Option B (For future short-selling support): Add a `PositionSide` field to `FillEvent`
distinguishing `OpenLong`, `CloseLong`, `OpenShort`, `CloseShort`. Route portfolio
accounting on `PositionSide`, not `Direction`. Requires full short-position tracking
(average entry, unrealised short P&L, margin). Mark short-selling as an explicit V2 scope
item and add unit tests covering the full short P&L cycle.

---

### BUG-05 — Monte Carlo Resamples Absolute PnL, Not Normalised Returns

**File:** `src/TradingResearchEngine.Application/Research/MonteCarloWorkflow.cs`

**Problem:**  
The Monte Carlo simulation resamples `ClosedTrade.NetPnl` (absolute USD values) and
reconstructs equity paths additively. If position sizes vary across trades, larger trades
dominate the path distribution and the simulation systematically overestimates risk from
large positions and underestimates it from small ones. The resulting confidence intervals
are biased.

**Required fix:**  
Resample per-trade **return on risk** rather than absolute PnL:
`tradeReturn = NetPnl / (EntryPrice * Quantity)`.
Reconstruct equity paths multiplicatively:
`equityPath[i] = equityPath[i-1] * (1 + sampledReturn)`.
The `ClosedTrade` record must expose `EntryPrice` and `Quantity` if it does not already.

---

## Medium Priority Improvements

### IMP-01 — Rolling SMA Calculation in SmaCrossoverStrategy

**File:** `src/TradingResearchEngine.Application/Strategies/SmaCrossoverStrategy.cs`

**Problem:**  
Both SMAs are recomputed from scratch on every bar using LINQ `Skip/Take/Average`,
which is O(n) per bar. With a 200-bar slow window over a 10,000-bar dataset this is
2,000,000 unnecessary additions.

**Required fix:**  
Replace with a rolling sum accumulator pattern. Maintain `_fastSum` and `_slowSum`
decimal fields, updated by adding the new close and subtracting the close that left the
window each bar. Guard correctly on window warmup. The SMA is then `_sum / period` — O(1)
per bar.

Apply the same rolling pattern to `MeanReversionStrategy`, `RsiStrategy` (rolling sums for
Wilder smoothing), and any other strategy that computes window statistics on every bar.

---

### IMP-02 — ADF Test in StationaryMeanReversionStrategy Runs Every Bar

**File:** `src/TradingResearchEngine.Application/Strategies/StationaryMeanReversionStrategy.cs`

**Problem:**  
The full ADF regression over the 500-bar lookback window runs on every bar. This makes
parameter sweeps with even a handful of parameter combinations very slow. Stationarity
regime does not change bar-by-bar.

**Required fix:**  
Cache the ADF result and only re-run the test every `_adfRecheckInterval` bars (add
constructor parameter `adfRecheckInterval = 20`, default 20). Between re-checks, use the
cached stationarity flag. This reduces ADF computation by ~95% with negligible impact on
signal quality.

Also fix the biased variance estimator on line `double varYl = sumYl2 / m - meanYl * meanYl`
— change `/ m` to `/ (m - 1)` for the unbiased sample variance. This slightly corrects
the t-statistic and is consistent with the standard ADF literature.

---

### IMP-03 — Replace Equity Curve Smoothness R² with K-Ratio

**File:** `src/TradingResearchEngine.Core/MetricsCalculator.cs`

**Problem:**  
`ComputeEquityCurveSmoothness` returns R² from an OLS regression on the equity curve. R²
measures linearity but not direction — a strategy that steadily loses money scores high R².
This metric is misleading in the backtest report.

**Required fix:**  
Replace with the **K-Ratio** (Zephyr/Kestner definition):

```
K-Ratio = (OLS slope of log-equity curve) / (standard error of slope * sqrt(n))
```

A positive K-Ratio indicates consistent upward progression; negative indicates consistent
decline. Higher absolute value = more linear progression. Update the `BacktestResult` type
and all reporters that reference the smoothness field. Keep the method name as
`ComputeEquityCurveSmoothness` but change the return semantic and update its XML doc.

---

### IMP-04 — Bid/Ask Awareness in SimulatedExecutionHandler

**File:** `src/TradingResearchEngine.Application/Execution/SimulatedExecutionHandler.cs`

**Problem:**  
`TickEvent` fill price currently uses `LastTrade.Price` for both buy and sell directions.
In real markets, buys fill at the ask and sells fill at the bid, creating a guaranteed
spread cost per round trip that the simulator currently ignores.

**Required fix:**  
Add `Quote Bid` and `Quote Ask` to `TickEvent` (or update the existing `ValueTypes.cs` with
a `Quote` record: `public record Quote(decimal Price, decimal Size)`). In
`SimulatedExecutionHandler`, route tick fills as:
- `Direction.Long` → fill at `tick.Ask.Price`
- `Direction.Flat` (close long) → fill at `tick.Bid.Price`

For `BarEvent` fills, the existing spread model (slippage) is sufficient as a proxy.

---

### IMP-05 — Limit and StopMarket Intra-Bar Fill Logic

**File:** `src/TradingResearchEngine.Application/Execution/SimulatedExecutionHandler.cs`

**Problem:**  
`OrderType.Limit` and `OrderType.StopMarket` orders currently appear to fill at the same
price as `Market` orders, which eliminates any meaningful difference between order types.

**Required fix:**  
Add intra-bar fill determination logic for `BarEvent`:

- **Limit buy**: only fill if `bar.Low <= order.LimitPrice`. Fill price = `order.LimitPrice`
  (pessimistic — do not assume price improvement).
- **Limit sell**: only fill if `bar.High >= order.LimitPrice`. Fill price = `order.LimitPrice`.
- **Stop market buy**: only fill if `bar.High >= order.StopPrice`. Fill price = `order.StopPrice`
  + slippage (triggered stop fills with adverse slippage).
- **Stop market sell**: only fill if `bar.Low <= order.StopPrice`. Fill price = `order.StopPrice`
  - slippage.
- If the fill condition is not met for the current bar, return `null` or a `NoFillResult` and
  keep the order in the pending queue for the next bar. Add `MaxBarsPending: int` to
  `OrderEvent` (default 0 = GTC) for automatic order expiry.

Add a `StopPrice` field to `OrderEvent` (alongside the existing `LimitPrice`) and add
`OrderType.StopLimit` to the `OrderType` enum as a V2 scope item.

---

## New Requirements for V2

### REQ-V2-01 — Equity Curve as First-Class Output

`Portfolio` must maintain a `List<EquityCurvePoint>` (or equivalent time-series) updated
on every bar (BUG-03 fix). `BacktestResult` must expose `IReadOnlyList<EquityCurvePoint>`.
`EquityCurvePoint` must include: `Timestamp`, `TotalEquity`, `CashBalance`,
`UnrealisedPnl`, `RealisedPnl`, `OpenPositionCount`.

### REQ-V2-02 — BarsPerYear Configuration

`ScenarioConfig` must expose `int BarsPerYear` (or equivalent) used by `MetricsCalculator`
for Sharpe/Sortino annualisation. Provide sensible defaults per common intervals:
Daily=252, H4=252*6=1512, H1=252*24=6048, M15=252*96=24192.

### REQ-V2-03 — FillMode Configuration

`ScenarioConfig` must expose `FillMode` enum (`NextBarOpen`, `SameBarClose`) defaulting
to `NextBarOpen`. All new tests must use `NextBarOpen`. Existing test fixtures that assert
exact fill prices may use `SameBarClose` temporarily during migration.

### REQ-V2-04 — ClosedTrade Must Expose EntryPrice and Quantity

`ClosedTrade` (or equivalent) must expose `decimal EntryPrice`, `decimal ExitPrice`,
`decimal Quantity`, `decimal NetPnl`, `decimal ReturnOnRisk` (computed property).

### REQ-V2-05 — All V2 Bug Fixes Must Have Unit Tests

For each of BUG-01 through BUG-05, at least one unit test must be added to the test project
that would have caught the bug in V1 and now passes with the fix. These are regression tests
and must be kept permanently.

Suggested test cases:
- BUG-01: Assert that a strategy signalling on bar N fills at bar N+1's Open, not bar N's Close.
- BUG-02: Assert that Sharpe computed from a flat equity curve is 0, and a linearly rising
  curve with known slope produces the expected ratio within tolerance.
- BUG-03: Assert that `TotalEquity` updates between fill events when an open position is
  marked to market.
- BUG-04: Assert that a `Direction.Flat` fill on a closed position does not increase cash
  without a corresponding position entry.
- BUG-05: Assert that Monte Carlo paths starting from the same seed are identical regardless
  of whether position size varies across trades (i.e., the path is normalised).

---

## Steering Document Updates Required

Kiro must update the following files as part of this spec:

**`.kiro/steering/product.md`**  
Add a `## V2 Scope` section describing the engine correctness overhaul. Note explicitly that
V2 is engine-only and that V3 will address the UI/UX rework. Keep the V1 scope section
intact.

**`.kiro/steering/domain-boundaries.md`**  
Update the `Core` owns list to include `EquityCurvePoint`, `FillMode`, `BarsPerYear`
on `ScenarioConfig`, and `ReturnOnRisk` on `ClosedTrade`. Note that `Direction.Short`
is removed from the enum (if Option A is chosen for BUG-04).

**`.kiro/steering/testing-standards.md`**  
Add a section `## Backtest Correctness Tests` describing the regression test requirement
from REQ-V2-05. State that any fix to engine-level numeric output must be accompanied by
a unit test demonstrating the fix.

**`.kiro/steering/tech.md`**  
No breaking tech stack changes in V2. Document that `BarsPerYear` is the canonical source
of truth for annualisation and must not be hardcoded elsewhere.

---

## Out of Scope for V2

- UI rework (explicitly V3)
- Live or paper trading
- Database persistence
- Named third-party data provider integrations
- Multi-currency portfolio tracking
- Short-selling (unless Option B for BUG-04 is chosen)
- Additional strategy implementations beyond bug fixes to existing ones

---

## Suggested Task Breakdown for tasks.md

The following tasks should be added to `.kiro/specs/trading-research-engine/tasks.md`:

| ID | Task | Depends on | Priority |
|----|------|------------|----------|
| V2-T01 | Add `FillMode` to `ScenarioConfig` and implement pending-order queue in `BacktestEngine` | — | Critical |
| V2-T02 | Add `MarkToMarket(symbol, price, timestamp)` to `Portfolio` and call from engine loop | V2-T01 | Critical |
| V2-T03 | Fix `ComputeSharpeRatio` and `ComputeSortinoRatio` to use equity curve period returns | V2-T02 | Critical |
| V2-T04 | Resolve `Direction.Short` ambiguity per chosen option (A or B) | — | Critical |
| V2-T05 | Fix Monte Carlo to resample `ReturnOnRisk`, reconstruct paths multiplicatively | — | Critical |
| V2-T06 | Add `BarsPerYear` to `ScenarioConfig`; thread through to `MetricsCalculator` | V2-T03 | Critical |
| V2-T07 | Add regression unit tests for BUG-01 through BUG-05 | V2-T01..V2-T05 | Critical |
| V2-T08 | Replace O(n) SMA in `SmaCrossoverStrategy` with rolling sum O(1) | — | Medium |
| V2-T09 | Add ADF recheck interval cache to `StationaryMeanReversionStrategy`; fix biased variance | — | Medium |
| V2-T10 | Replace R² smoothness with K-Ratio in `MetricsCalculator` | V2-T03 | Medium |
| V2-T11 | Add `Quote Bid/Ask` to `TickEvent`; implement bid/ask-aware fill in `SimulatedExecutionHandler` | — | Medium |
| V2-T12 | Implement intra-bar limit and stop-market fill logic in `SimulatedExecutionHandler` | V2-T01 | Medium |
| V2-T13 | Expose `EquityCurvePoint` list on `BacktestResult`; update reporters | V2-T02 | Medium |
| V2-T14 | Update all `.kiro/steering/` docs per steering update section above | — | Medium |
| V2-T15 | Update `requirements.md` and `design.md` to reflect V2 changes | — | Medium |

---

## Acceptance Criteria for V2

V2 is considered complete when:

1. A `SmaCrossoverStrategy` backtest on the same dataset produces a **different** (lower)
   equity curve peak than V1, confirming look-ahead bias was eliminated.
2. Sharpe ratio on a strategy with no trades between two fills differs in value compared to
   V1, confirming equity-curve-based computation is in effect.
3. `Portfolio.TotalEquity` changes between fill events when a position is open and a new
   bar arrives, confirming continuous mark-to-market.
4. No `Direction.Short` ambiguity exists — either the enum value is removed or full short
   accounting is verified by unit tests.
5. Monte Carlo paths from the same seed with variable position size produce the same
   distribution shape as paths with fixed position size (normalisation is effective).
6. All existing V1 tests pass (or are documented as intentionally superseded by V2 tests).
7. All V2 regression tests (REQ-V2-05) pass.
