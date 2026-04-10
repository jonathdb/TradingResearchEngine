# Requirements Document

## Introduction

Two bugs have been identified in the Dukascopy data pipeline that produce incorrect output data:

1. **Interval resolution bug**: `IntervalToMinutes()` in `DukascopyHelpers` silently falls back to daily (1440 minutes) for any unrecognized interval string, including valid casing variants like `"1H"`. This causes the provider to return daily bars regardless of the requested timeframe, with no error or log warning. A sample output file (`dukascopy_EURUSD_1H_20260201_20260410.csv`) confirmed this: all 49 rows have `T00:00:00+00:00` timestamps, one per trading day, despite the filename indicating `1H` resolution.

2. **OHLC integrity bug**: The `Aggregate()` method in `DukascopyHelpers` does not guard the aggregation window's `open` or final `close` against the running `high`/`low` values. When the source minute bar contains `Open > High` (which can occur due to bid/ask spread effects in the binary parsing), the window's recorded `Open` exceeds its `High`, producing an invalid OHLC bar. The same sample file contained two such bars:
   - `2026-02-23`: `Open=1.18333`, `High=1.18328` (High < Open by 0.5 pips)
   - `2026-03-31`: `Close=1.15743`, `High=1.15739` (Close > High by 0.4 pips)

In addition, the following broader issues have been identified in the Dukascopy provider and are included in this spec for a single consolidated review:

3. **Cache key uses full date range**: The cache path is keyed as `{SYMBOL}_{INTERVAL}_{from:yyyyMMdd}_{to:yyyyMMdd}.csv`, so a request for a superset or overlapping date range always misses the cache and re-downloads all data. This causes redundant downloads and can produce silent data gaps on partial-overlap requests.

4. **Tick data not implemented**: `GetTicks()` returns `EmptyAsyncEnumerable<TickRecord>.Instance` with a warning log. Dukascopy publishes per-hour tick files at `ASK_ticks_{hour}.bi5` / `BID_ticks_{hour}.bi5` using a 20-byte record format. Any caller of `GetTicks()` receives no data with no visible error.

5. **Only BID candles fetched; no ASK or mid-price**: The download URL is hardcoded to `BID_candles_min_1.bi5`. For spread-aware forex strategies, OHLC is consistently understated because true fill price for buys is the ask. There is no option to fetch `ASK_candles_min_1.bi5` or compute a midpoint.

6. **`uncompressedSize` extraction is platform-fragile**: `Decompress()` reads the uncompressed size with `BitConverter.ToInt64(data, 5)`, which relies on host endianness. On little-endian x86/x64 this is correct, but on big-endian platforms it would silently produce garbage. Using `BinaryPrimitives.ReadInt64LittleEndian` makes the intent explicit and portable.

7. **Session boundary alignment for non-multiple intervals**: `TruncateToInterval` truncates by total minutes from midnight UTC. For intervals like `4h`, this correctly produces boundaries at 00:00, 04:00, 08:00, etc. However, if the first downloaded minute bar in a session does not fall exactly on a boundary, the window opens at the correct boundary time but uses the first available bar as the open — it does not back-fill or mark the bar as partial, which can silently understate session volume and misrepresent the true opening bar.

8. **HTTP errors are silently swallowed**: `HttpRequestException` is caught and returns an empty list with no retry. A single transient network failure produces a silent hole in the data that downstream consumers cannot detect.

All bugs affect correctness of backtests or analytics that consume Dukascopy data. This spec defines the fixes and regression tests.

## Key Architecture Decisions

1. Only `DukascopyHelpers.cs` and `DukascopyDataProvider.cs` are modified. No changes to Core types, no changes to any other provider, no changes to the canonical CSV schema.
2. `IntervalToMinutes()` throws `ArgumentException` for unrecognized interval strings rather than silently falling back.
3. OHLC integrity is enforced at the point of aggregation, not in post-processing.
4. The per-day cache layout replaces the monolithic date-range file. Existing cache files are not migrated — they are simply not found and regenerated on the next request.
5. Tick data implementation is **required** (not optional) per this spec; it must correctly parse the 20-byte record format.
6. ASK candle download is **opt-in via a constructor/config parameter**; the default remains BID-only for backwards compatibility.
7. `BinaryPrimitives.ReadInt64LittleEndian` replaces `BitConverter.ToInt64` in `Decompress()`.
8. Partial session bars are logged as warnings; they are not silently discarded and not silently emitted without annotation.
9. HTTP retry uses `Polly` (or `HttpClientFactory` retry policy) with exponential back-off, maximum 3 attempts, before throwing.

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

### Requirement 3 — Per-Day Cache Strategy

**User Story:** As a developer, I want the cache to be keyed per trading day rather than per full date range, so that incremental fetches reuse cached days and never redundantly re-download data for a date range that was partially cached.

#### Acceptance Criteria

1. THE cache path for a single day's candles SHALL follow the pattern: `{CacheDir}/{symbol}/{yyyy}/{MM}/{dd}.csv`, where `{yyyy}`, `{MM}`, and `{dd}` are derived from the trading day's UTC date.
2. WHEN loading data for a date range, `DukascopyDataProvider` SHALL check and load each individual day's cache file independently; days already cached SHALL NOT trigger an HTTP download.
3. WHEN a day's HTTP download succeeds, the result SHALL be written to that day's cache file before being yielded to the caller.
4. THE old monolithic `{SYMBOL}_{INTERVAL}_{from}_{to}.csv` cache path SHALL be removed. No migration of existing files is required.
5. CACHE files that exist but are zero bytes or contain only a header row SHALL be treated as missing (the day is re-fetched).
6. THE cache directory structure SHALL be created automatically if it does not exist.

---

### Requirement 4 — Tick Data Implementation

**User Story:** As a developer, I want `GetTicks()` to return real tick data from Dukascopy, so that strategies requiring sub-minute granularity have a working data source.

#### Acceptance Criteria

1. `GetTicks()` SHALL download per-hour tick files from Dukascopy using the URL pattern: `https://datafeed.dukascopy.com/datafeed/{SYMBOL}/{YEAR}/{MONTH:D2}/{DAY:D2}/{HOUR:D2}h_ticks.bi5`.
2. EACH tick file SHALL be decompressed using the same LZMA logic as candle files.
3. EACH tick record IS 20 bytes with the following layout (all values big-endian):
   - Bytes 0–3: timestamp offset from the start of the hour, in milliseconds (uint32)
   - Bytes 4–7: ask price × 100000 (uint32)
   - Bytes 8–11: bid price × 100000 (uint32)
   - Bytes 12–15: ask volume (float32)
   - Bytes 16–19: bid volume (float32)
4. THE absolute timestamp of each tick SHALL be computed as `hourStart + TimeSpan.FromMilliseconds(offset)`.
5. TICK records with `ask <= 0` OR `bid <= 0` SHALL be discarded.
6. ALL 24 hours per trading day SHALL be fetched concurrently (with the same degree of parallelism as the candle batch download).
7. THE existing warning log in `GetTicks()` SHALL be removed once the implementation is complete.
8. TICKS outside the requested `[from, to]` date range SHALL be filtered before being yielded.

---

### Requirement 5 — ASK Candles and Mid-Price Option

**User Story:** As a researcher, I want the option to fetch ASK candles alongside BID candles and receive mid-price OHLC bars, so that spread-aware strategies use realistic fill-price data.

#### Acceptance Criteria

1. `DukascopyDataProvider` SHALL accept a constructor or configuration parameter `PriceType` with values: `Bid` (default), `Ask`, `Mid`.
2. WHEN `PriceType` is `Ask`, the download URL SHALL use `ASK_candles_min_1.bi5` instead of `BID_candles_min_1.bi5`.
3. WHEN `PriceType` is `Mid`, BOTH `BID_candles_min_1.bi5` AND `ASK_candles_min_1.bi5` SHALL be fetched for each day, and the output bar SHALL be computed as the average of the two: `MidOHLC = (BidOHLC + AskOHLC) / 2` for each of Open, High, Low, Close; volume is taken from the bid file.
4. THE existing default behaviour (BID-only) SHALL be unchanged when `PriceType` is not specified.
5. THE cache path SHALL incorporate the `PriceType` to prevent cross-contamination: `{CacheDir}/{symbol}/{priceType}/{yyyy}/{MM}/{dd}.csv`.

---

### Requirement 6 — Portable Uncompressed-Size Reading

**User Story:** As a developer, I want the LZMA decompression to use an explicit little-endian read for the uncompressed size field, so that the code is correct by construction on all platforms.

#### Acceptance Criteria

1. THE call `BitConverter.ToInt64(data, 5)` in `Decompress()` SHALL be replaced with `BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(5, 8))`.
2. THE `System.Buffers.Binary` namespace SHALL be imported where needed.
3. THE logical behaviour SHALL be identical on little-endian platforms; on big-endian platforms the result SHALL now be correct rather than producing garbage.
4. NO other changes to the decompression logic are required by this requirement.

---

### Requirement 7 — Partial Session Bar Logging

**User Story:** As a developer, I want the provider to log a warning when the first minute bar in a session does not fall on an aggregation window boundary, so that I can detect and reason about incomplete opening bars.

#### Acceptance Criteria

1. WHEN `TruncateToInterval` maps the first bar of a day to a window boundary that precedes the bar's own timestamp (i.e. the window has no data for the minutes before the first bar), the provider SHALL log a warning at `LogLevel.Warning` containing the symbol, date, interval, window boundary, and first bar timestamp.
2. THE bar SHALL still be emitted — it is not discarded. The warning is informational only.
3. NO new field is added to `BarRecord` to mark partiality; the warning log is the sole signal.
4. THE warning SHALL appear at most once per trading day per symbol, not once per bar.

---

### Requirement 8 — HTTP Retry with Exponential Back-Off

**User Story:** As a developer, I want transient HTTP failures to be retried automatically so that a single network hiccup does not silently produce a hole in the downloaded data.

#### Acceptance Criteria

1. EACH individual HTTP request to Dukascopy (both candle and tick files) SHALL be retried up to **3 times** on `HttpRequestException` or HTTP 5xx response codes before the error is propagated.
2. THE retry delay SHALL use exponential back-off: attempt 1 waits 1 s, attempt 2 waits 2 s, attempt 3 waits 4 s.
3. HTTP 404 responses SHALL NOT be retried; a missing file (e.g. no tick data for an off-market hour) is not an error and SHALL return an empty result.
4. AFTER all retry attempts are exhausted, the exception SHALL be logged at `LogLevel.Error` with the URL, attempt count, and exception message, then re-thrown so the caller is aware of the failure.
5. The retry logic SHALL be implemented using Polly (`Microsoft.Extensions.Http.Polly` or `Polly` directly) or the `HttpClientFactory` built-in retry handler — not a manual loop.

---

### Requirement 9 — Unit Tests

**User Story:** As a developer, I want unit tests that cover all fixes in this spec, so that regressions are caught automatically.

#### Acceptance Criteria

1. UNIT tests SHALL be added to `TradingResearchEngine.UnitTests` in a class named `DukascopyHelpersTests`.
2. THE following **interval parsing** cases SHALL be tested:
   - `"1m"`, `"5m"`, `"15m"`, `"30m"`, `"1h"`, `"1H"`, `"60m"`, `"4h"`, `"4H"`, `"1d"`, `"1D"`, `"daily"`, `"Daily"` — all return the correct minute count.
   - `"H1"`, `"hourly"`, `"1 hour"`, `""`, `null` — all throw `ArgumentException`.
3. THE following **OHLC aggregation** cases SHALL be tested:
   - A window where the first bar has `Open > High`: output bar's `High >= Open`.
   - A window where the last bar has `Close > High`: output bar's `High >= Close`.
   - A window where the first bar has `Open < Low`: output bar's `Low <= Open`.
   - A normal window with clean input bars: output OHLC is unchanged.
   - An empty input list: `Aggregate()` returns an empty list without throwing.
4. THE following **cache key** cases SHALL be tested:
   - `GetCachePath("EURUSD", new DateTime(2024, 3, 5), "Bid")` returns a path ending in `EURUSD/Bid/2024/03/05.csv`.
   - Two different date ranges that share an overlapping day both resolve to the same file path for that day.
5. THE following **tick record parsing** cases SHALL be tested:
   - A correctly formed 20-byte record is parsed to the expected ask, bid, and timestamp values.
   - A record with `ask = 0` is discarded.
   - An input byte array whose length is not a multiple of 20 does not throw; the trailing incomplete record is discarded.
6. THE following **`Decompress()` endianness** case SHALL be tested (pure unit test, no HTTP):
   - A known-good byte slice with a little-endian int64 at offset 5 is read correctly by `BinaryPrimitives.ReadInt64LittleEndian`.
7. ALL tests SHALL follow the naming convention `<Method>_<Condition>_<Expected>` and be grouped under `DukascopyHelpersTests`.
8. NO tests SHALL make real HTTP calls.
