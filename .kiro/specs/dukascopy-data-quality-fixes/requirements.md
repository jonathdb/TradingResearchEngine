# Requirements — Dukascopy Data Quality Fixes

## Introduction

Two bugs have been identified in the Dukascopy data pipeline that produce incorrect output data:

1. **Interval resolution bug**: `IntervalToMinutes()` in `DukascopyHelpers` silently falls back to daily (1440 minutes) for any unrecognized interval string, including valid casing variants like `"1H"`. This causes the provider to return daily bars regardless of the requested timeframe, with no error or log warning. A sample output file (`dukascopy_EURUSD_1H_20260201_20260410.csv`) confirmed this: all 49 rows have `T00:00:00+00:00` timestamps, one per trading day, despite the filename indicating `1H` resolution.

2. **OHLC integrity bug**: The `Aggregate()` method in `DukascopyHelpers` does not guard the aggregation window's `open` or final `close` against the running `high`/`low` values. When the source minute bar contains `Open > High` (which can occur due to bid/ask spread effects in the binary parsing), the window's recorded `Open` exceeds its `High`, producing an invalid OHLC bar. The same sample file contained two such bars:
   - `2026-02-23`: `Open=1.18333`, `High=1.18328` (High < Open by 0.5 pips)
   - `2026-03-31`: `Close=1.15743`, `High=1.15739` (Close > High by 0.4 pips)

Both bugs affect correctness of any backtest or analytics that consumes Dukascopy data. This spec defines the fixes and regression tests.

### Key Architecture Decisions

1. Only `DukascopyHelpers.cs` is modified. `DukascopyDataProvider.cs` is unchanged except where it calls the fixed helpers.
2. `IntervalToMinutes()` throws `ArgumentException` for unrecognized interval strings rather than silently falling back. All call sites that previously relied on the default are updated.
3. OHLC integrity is enforced at the point of aggregation, not in post-processing. Each output bar guarantees `Low ≤ Open ≤ High`, `Low ≤ Close ≤ High`.
4. A new `ValidateOhlc` guard method is added to `DukascopyHelpers` and applied in both `ParseCandles` (per source bar) and `Aggregate` (per output window).
5. No changes to Core types, no changes to the canonical CSV schema, no changes to any other provider.

---

## Requirements

### Requirement 1 — Strict Interval Parsing

**User Story:** As a developer, I want `IntervalToMinutes()` to throw a descriptive exception on unrecognized interval strings, so that misconfigured callers fail loudly instead of silently producing daily bars.

#### Acceptance Criteria

1. `IntervalToMinutes()` SHALL match interval strings case-insensitively (the existing `.ToLowerInvariant()` call is sufficient).
2. THE supported mappings SHALL be: `"1m"` → 1, `"5m"` → 5, `"15m"` → 15, `"30m"` → 30, `"1h"` → 60, `"60m"` → 60, `"4h"` → 240, `"1d"` → 1440, `"daily"` → 1440.
3. ANY interval string that does not match a supported mapping SHALL cause `IntervalToMinutes()` to throw `ArgumentException` with a message that includes the unrecognized value and the list of supported values.
4. THE fallback `_ => 1440` arm in the switch expression SHALL be removed.
5. ALL existing callers of `IntervalToMinutes()` within the solution SHALL be reviewed and confirmed to pass only supported interval strings.

---

### Requirement 2 — OHLC Integrity in Aggregation

**User Story:** As a researcher, I want every aggregated bar to have a valid OHLC relationship, so that strategies and analytics never receive bars where `High < Open`, `High < Close`, `Low > Open`, or `Low > Close`.

#### Acceptance Criteria

1. AFTER aggregating a window, the output `BarRecord` SHALL satisfy: `Low ≤ Open ≤ High` AND `Low ≤ Close ≤ High`.
2. THE `Aggregate()` method SHALL initialize `high` as `Max(bars[i].High, bars[i].Open)` and `low` as `Min(bars[i].Low, bars[i].Open)` for the first bar in each window.
3. AT THE END of each aggregation window, before emitting the `BarRecord`, the method SHALL clamp: `high = Max(high, close)` and `low = Min(low, close)`.
4. THE `ParseCandles()` method SHALL skip any source bar where the parsed values violate basic sanity: `open <= 0`, `high <= 0`, `low <= 0`, `close <= 0`, or `high < low`. Such records are silently discarded (existing behaviour for zero values is retained).
5. A private static helper `ClampOhlc(decimal open, decimal high, decimal low, decimal close)` (or equivalent inline logic) SHALL be the single authoritative enforcement point used by `Aggregate()`.

---

### Requirement 3 — Unit Tests

**User Story:** As a developer, I want unit tests that cover both the interval parsing fix and the OHLC integrity fix, so that regressions are caught automatically.

#### Acceptance Criteria

1. UNIT tests SHALL be added to `TradingResearchEngine.UnitTests` in a class named `DukascopyHelpersTests`.
2. THE following interval parsing cases SHALL be tested:
   - `"1m"`, `"5m"`, `"15m"`, `"30m"`, `"1h"`, `"1H"`, `"60m"`, `"4h"`, `"4H"`, `"1d"`, `"1D"`, `"daily"`, `"Daily"` — all return the correct minute count.
   - `"H1"`, `"hourly"`, `"1 hour"`, `""`, `null` — all throw `ArgumentException`.
3. THE following OHLC aggregation cases SHALL be tested:
   - A window where the first bar has `Open > High` (source data anomaly): the output bar's `High >= Open`.
   - A window where the last bar has `Close > High` (source data anomaly): the output bar's `High >= Close`.
   - A window where the first bar has `Open < Low` (source data anomaly): the output bar's `Low <= Open`.
   - A normal window with clean input bars: the output OHLC is unchanged (no clamping applied).
   - An empty input list: `Aggregate()` returns an empty list without throwing.
4. ALL tests SHALL follow the existing naming convention `<Method>_<Condition>_<Expected>` and be grouped under `DukascopyHelpersTests`.
5. NO tests SHALL make real HTTP calls.
