# Implementation Plan — Dukascopy Data Quality Fixes

- [ ] 1. Fix `IntervalToMinutes` in `DukascopyHelpers`

  - [ ] 1.1 Remove the `_ => 1440` fallback arm from the switch expression in `IntervalToMinutes()`

    - Replace with `var unknown => throw new ArgumentException(...)` that includes the unrecognized value and lists supported values
    - Ensure `.ToLowerInvariant()` is applied before the switch so `"1H"`, `"4H"`, `"1D"`, `"Daily"` all resolve correctly
    - _Requirements: 1.1, 1.2, 1.3, 1.4_

  - [ ] 1.2 Review all callers of `IntervalToMinutes()` in the solution

    - Confirm every call site passes a value from the supported set: `1m, 5m, 15m, 30m, 1h, 60m, 4h, 1d, daily`
    - Check `DukascopyDataProvider.GetBars()`, `DukascopyHelpers.Aggregate()`, and any test helpers
    - _Requirements: 1.5_

- [ ] 2. Fix OHLC integrity in `Aggregate()`

  - [ ] 2.1 Fix window initialisation for the first bar of each aggregation window

    - Change `decimal high = bars[i].High` to `decimal high = Math.Max(bars[i].High, bars[i].Open)`
    - Change `decimal low = bars[i].Low` to `decimal low = Math.Min(bars[i].Low, bars[i].Open)`
    - _Requirements: 2.1, 2.2_

  - [ ] 2.2 Add close-clamping before emitting each window bar

    - Before the `result.Add(...)` call, add: `high = Math.Max(high, close);` and `low = Math.Min(low, close);`
    - _Requirements: 2.1, 2.3_

- [ ] 3. Extend source bar sanity filter in `ParseCandles()`

  - [ ] 3.1 Add `h >= l` to the existing positivity guard

    - Change `if (o > 0 && h > 0 && l > 0 && c > 0)` to `if (o > 0 && h > 0 && l > 0 && c > 0 && h >= l)`
    - _Requirements: 2.4_

- [ ] 4. Write unit tests in `DukascopyHelpersTests`

  - [ ] 4.1 Create `src/TradingResearchEngine.UnitTests/DukascopyHelpersTests.cs`

    - Add class `DukascopyHelpersTests` following existing test naming conventions
    - _Requirements: 3.1_

  - [ ] 4.2 Add interval parsing tests — valid inputs

    - `[Theory]` covering: `1m`, `5m`, `15m`, `30m`, `1h`, `1H`, `60m`, `4h`, `4H`, `1d`, `1D`, `daily`, `Daily`
    - Each asserts the correct minute count is returned
    - _Requirements: 3.2_

  - [ ] 4.3 Add interval parsing tests — invalid inputs

    - `[Theory]` covering: `H1`, `hourly`, `1 hour`, `""`, `bad`
    - Each asserts `ArgumentException` is thrown
    - Separate `[Fact]` for `null` input asserting any exception is thrown
    - _Requirements: 3.2_

  - [ ] 4.4 Add OHLC aggregation tests

    - `Aggregate_FirstBarOpenExceedsHigh_OutputHighCoversOpen`
    - `Aggregate_LastBarCloseExceedsHigh_OutputHighCoversClose`
    - `Aggregate_FirstBarOpenBelowLow_OutputLowCoversOpen`
    - `Aggregate_CleanInput_OhlcUnchanged`
    - `Aggregate_EmptyInput_ReturnsEmpty`
    - _Requirements: 3.3_

  - [ ] 4.5 Verify all new tests pass and no existing tests regress

    - Run full unit test suite; confirm zero failures
    - _Requirements: 3.4, 3.5_
