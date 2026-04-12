# Implementation Plan — Dukascopy Data Quality Fixes

- [x] 1. Fix `IntervalToMinutes` in `DukascopyHelpers`

  - [x] 1.1 Add null guard and remove the `_ => 1440` fallback arm

    - Add `ArgumentNullException.ThrowIfNull(interval);` as the first line of the method body (convert from expression-bodied to block-bodied method)
    - Replace `_ => 1440` with `var unknown => throw new ArgumentException(...)` that includes the unrecognized value and lists supported values
    - Ensure `.ToLowerInvariant()` is applied before the switch so `"1H"`, `"4H"`, `"1D"`, `"Daily"` all resolve correctly
    - _Requirements: 1.1, 1.2, 1.3, 1.4_

  - [x] 1.2 Review all callers of `IntervalToMinutes()` in the solution

    - Confirm every call site passes a value from the supported set: `1m, 5m, 15m, 30m, 1h, 60m, 4h, 1d, daily`
    - Check `DukascopyDataProvider.GetBars()`, `DukascopyHelpers.Aggregate()`, `DukascopyImportProvider`, and any test helpers
    - Note: `DukascopyImportProvider.AllTimeframes` uses `"1H"`, `"4H"`, `"Daily"` — these are safe because `.ToLowerInvariant()` normalizes them before the switch. No changes needed in the import provider.
    - _Requirements: 1.5_

- [x] 2. Fix OHLC integrity in `Aggregate()` and `ParseCandles()`

  - [x] 2.1 Fix window initialisation for the first bar of each aggregation window

    - Change `decimal high = bars[i].High` to `decimal high = Math.Max(bars[i].High, bars[i].Open)`
    - Change `decimal low = bars[i].Low` to `decimal low = Math.Min(bars[i].Low, bars[i].Open)`
    - _Requirements: 2.1, 2.2_

  - [x] 2.2 Add close-clamping before emitting each window bar

    - Before the `result.Add(...)` call, add: `high = Math.Max(high, close);` and `low = Math.Min(low, close);`
    - _Requirements: 2.1, 2.3_

  - [x] 2.3 Add `h >= l` guard to `ParseCandles()` source bar filter

    - Change `if (o > 0 && h > 0 && l > 0 && c > 0)` to `if (o > 0 && h > 0 && l > 0 && c > 0 && h >= l)`
    - _Requirements: 2.4_

- [x] 3. Implement per-day cache strategy in `DukascopyDataProvider`

  - [x] 3.1 Add `GetDayCachePath` helper to `DukascopyHelpers`

    - Signature: `GetDayCachePath(string cacheDir, string symbol, string priceType, DateTime date)` — include `priceType` in the final signature now so Task 5 does not need to amend it later
    - Returns `{cacheDir}/{symbol}/{priceType}/{yyyy}/{MM}/{dd}.csv`
    - Create directory structure automatically if it does not exist
    - _Requirements: 3.1, 3.6, 5.5_

  - [x] 3.2 Refactor `DukascopyDataProvider.GetBars()` to use per-day cache

    - Check and load each day's cache file independently; skip HTTP for cached days
    - Write each day's result to its cache file after successful download
    - Treat zero-byte or header-only cache files as missing (re-fetch)
    - Remove the old monolithic `{SYMBOL}_{INTERVAL}_{from}_{to}.csv` cache path
    - _Requirements: 3.2, 3.3, 3.4, 3.5_

- [x] 4. Implement tick data in `DukascopyDataProvider.GetTicks()`

  - [x] 4.1 Add `ParseTicks` method to `DukascopyHelpers`

    - Parse 20-byte big-endian records: ms offset (uint32), ask (uint32), bid (uint32), ask vol (float32), bid vol (float32)
    - Compute absolute timestamp as `hourStart + TimeSpan.FromMilliseconds(offset)`
    - Map to `TickRecord` using single-element bid/ask lists: `new[] { new BidLevel(bidPrice, bidVol) }`, `new[] { new AskLevel(askPrice, askVol) }`
    - Synthesize `LastTrade` from mid-price `(ask + bid) / 2` with size `Math.Min(askVol, bidVol)` and the tick's timestamp
    - Add XML doc comment noting that `LastTrade` is provider-derived (not exchange-reported) since Dukascopy provides top-of-book only
    - Discard records with `ask <= 0` or `bid <= 0`
    - Discard trailing incomplete records (byte length not multiple of 20)
    - _Requirements: 4.2, 4.3, 4.4, 4.5_

  - [x] 4.2 Implement `GetTicks()` in `DukascopyDataProvider`

    - Download per-hour tick files using URL pattern: `{BaseUrl}/{SYMBOL}/{YEAR}/{MONTH:D2}/{DAY:D2}/{HOUR:D2}h_ticks.bi5`
    - Fetch all 24 hours per trading day concurrently (same parallelism as candle batch)
    - Decompress using existing LZMA logic, then parse with `ParseTicks`
    - Filter ticks outside the requested `[from, to]` range before yielding
    - Remove the existing warning log
    - _Requirements: 4.1, 4.6, 4.7, 4.8_

- [x] 5. Add ASK candles and mid-price option

  - [x] 5.1 Add `DukascopyPriceType` enum and constructor parameter to `DukascopyDataProvider`

    - Enum values: `Bid` (default), `Ask`, `Mid`
    - Default behaviour unchanged when `PriceType` is not specified
    - _Requirements: 5.1, 5.4_

  - [x] 5.2 Update `BuildDayUrl` to accept price type via a new overload

    - Add overload: `BuildDayUrl(string symbol, DateTime date, DukascopyPriceType priceType)` — `Bid` → `BID_candles_min_1.bi5`, `Ask` → `ASK_candles_min_1.bi5`
    - Keep original 2-arg `BuildDayUrl(string symbol, DateTime date)` delegating to the new overload with `DukascopyPriceType.Bid` — this preserves `DukascopyImportProvider` compatibility without changes
    - _Requirements: 5.2_

  - [x] 5.3 Implement mid-price computation in `GetBars()` when `PriceType == Mid`

    - Fetch both BID and ASK files for each day
    - Compute `MidOHLC = (BidOHLC + AskOHLC) / 2` for each bar; volume from BID file
    - If ASK file fails but BID succeeds, treat the day as a download failure (no partial mid-price bar)
    - _Requirements: 5.3_

- [x] 6. Fix portable endianness in `Decompress()`

  - [x] 6.1 Replace `BitConverter.ToInt64(data, 5)` with `BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(5, 8))`

    - Import `System.Buffers.Binary` namespace if not already present
    - No other changes to decompression logic
    - _Requirements: 6.1, 6.2, 6.3, 6.4_

- [x] 7. Add partial session bar warning logging

  - [x] 7.1 Detect and log partial opening bars in `GetBars()`

    - Detection lives in `DukascopyDataProvider.GetBars()`, after collecting all minute bars for a day and before calling `Aggregate()` — compare the first bar's timestamp against `TruncateToInterval` for that bar
    - When the truncated boundary precedes the bar's timestamp, log at `LogLevel.Warning` with symbol, date, interval, window boundary, and first bar timestamp
    - Bar is still emitted; warning is informational only
    - Warning fires at most once per trading day per symbol
    - _Requirements: 7.1, 7.2, 7.3, 7.4_

- [x] 8. Add HTTP retry with exponential back-off

  - [x] 8.1 Add Polly retry policy to `DukascopyDataProvider` HTTP requests

    - Use a local `AsyncRetryPolicy` from `Polly` (not `HttpClientFactory`-based) wrapping each `HttpClient` call inside `DukascopyDataProvider` — this avoids changing the DI registration or `HttpClient` setup
    - Retry up to 3 times on `HttpRequestException` or HTTP 5xx responses
    - Exponential back-off: 1s → 2s → 4s
    - HTTP 404 is not retried; returns empty result
    - After exhausting retries, log at `LogLevel.Error` with URL, attempt count, and exception message, then re-throw
    - Apply to both candle and tick file downloads
    - Add `Polly` NuGet package to `TradingResearchEngine.Infrastructure`
    - Scope: `DukascopyDataProvider` only — `DukascopyImportProvider` retains its existing manual retry loop (consolidation is a separate follow-up spec)
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5_

- [x] 9. Write tests in `DukascopyHelpersTests` (in `IntegrationTests/MarketData/DukascopyHelpersTests.cs`, extending the existing class)

  - [x] 9.1 Add interval parsing tests — valid inputs

    - `[Theory]` covering: `1m`, `5m`, `15m`, `30m`, `1h`, `1H`, `60m`, `4h`, `4H`, `1d`, `1D`, `daily`, `Daily`
    - Each asserts the correct minute count is returned
    - _Requirements: 9.2_

  - [x] 9.2 Add interval parsing tests — invalid inputs

    - `[Theory]` covering: `H1`, `hourly`, `1 hour`, `""`, `bad` — each asserts `ArgumentException`
    - Separate `[Fact]` for `null` input asserting `ArgumentNullException`
    - _Requirements: 9.2_

  - [x] 9.3 Add OHLC aggregation tests

    - `Aggregate_FirstBarOpenExceedsHigh_OutputHighCoversOpen`
    - `Aggregate_LastBarCloseExceedsHigh_OutputHighCoversClose`
    - `Aggregate_FirstBarOpenBelowLow_OutputLowCoversOpen`
    - `Aggregate_CleanInput_OhlcUnchanged`
    - `Aggregate_EmptyInput_ReturnsEmpty`
    - _Requirements: 9.3_

  - [x] 9.4 Add cache path tests

    - `GetDayCachePath_ReturnsCorrectStructure` — path ends in `EURUSD/Bid/2024/03/05.csv`
    - `GetDayCachePath_OverlappingRanges_SameDaySamePath` — overlapping ranges resolve to same path for shared day
    - _Requirements: 9.4_

  - [x] 9.5 Add tick parsing tests

    - `ParseTicks_ValidRecord_ReturnsExpectedValues` — well-formed 20-byte record parsed correctly
    - `ParseTicks_ZeroAsk_Discarded` — record with ask = 0 is discarded
    - `ParseTicks_IncompleteTrailingRecord_Discarded` — no throw; trailing bytes ignored
    - _Requirements: 9.5_

  - [x] 9.6 Add endianness test

    - `Decompress_LittleEndianSize_ReadCorrectly` — known byte slice at offset 5 read correctly by `BinaryPrimitives.ReadInt64LittleEndian`
    - _Requirements: 9.6_

  - [x] 9.7 Verify all tests pass and no existing tests regress

    - Run full test suite; confirm zero failures
    - _Requirements: 9.7, 9.8_
