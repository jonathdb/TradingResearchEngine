# Design — Dukascopy Data Quality Fixes

## Overview

This document describes the targeted code changes to `DukascopyHelpers.cs` and the accompanying unit tests in `DukascopyHelpersTests`. No other files require modification.

---

## 1. `IntervalToMinutes` — Throw on Unrecognized Input

### Current Code

```csharp
public static int IntervalToMinutes(string interval) => interval.ToLowerInvariant() switch
{
    "1m" => 1, "5m" => 5, "15m" => 15, "30m" => 30,
    "1h" or "60m" => 60, "4h" => 240, "1d" or "daily" => 1440,
    _ => 1440   // ← BUG: silently returns daily for any unrecognized string
};
```

### Problem

The wildcard arm `_ => 1440` means that `"1H"`, `"H1"`, `"hourly"`, or any typo produces daily bars with no diagnostic. The sample data file (`dukascopy_EURUSD_1H_20260201_20260410.csv`) confirmed that a caller passes `"1H"` (uppercase H), which was already handled by `.ToLowerInvariant()` — however, the real risk is that *any* unrecognized string silently degrades to daily resolution.

### Fix

Replace `_ => 1440` with a throw:

```csharp
public static int IntervalToMinutes(string interval)
{
    return interval.ToLowerInvariant() switch
    {
        "1m"              => 1,
        "5m"              => 5,
        "15m"             => 15,
        "30m"             => 30,
        "1h" or "60m"     => 60,
        "4h"              => 240,
        "1d" or "daily"   => 1440,
        var unknown       => throw new ArgumentException(
            $"Unrecognized interval '{unknown}'. Supported values: 1m, 5m, 15m, 30m, 1h, 60m, 4h, 1d, daily.",
            nameof(interval))
    };
}
```

Note: the switch now uses a `var` discard arm to capture the unrecognized value for the error message. The `.ToLowerInvariant()` call means `"1H"`, `"4H"`, `"1D"`, `"Daily"` etc. all match correctly.

### Caller Review

All callers of `IntervalToMinutes()` must pass a supported string. The known call sites are:
- `DukascopyHelpers.Aggregate()` — passes the `interval` argument forwarded from `DukascopyDataProvider.GetBars()`, which originates from the `IDataProvider` contract. The `market-data-acquisition` spec requires the Web UI to restrict the timeframe selector to supported values (`1m, 5m, 15m, 30m, 1H, 4H, Daily`). These all normalise correctly after `.ToLowerInvariant()`.
- Any test helpers — must be updated to pass a valid string.

---

## 2. `Aggregate` — OHLC Integrity Guards

### Current Code

```csharp
decimal open   = bars[i].Open;
decimal high   = bars[i].High;   // ← Does NOT consider Open
decimal low    = bars[i].Low;
decimal close  = bars[i].Close;
// ...
result.Add(new BarRecord(symbol, interval, open, high, low, close, volume, timestamp));
// ← Does NOT clamp close against high/low before emitting
```

### Problem

If a source minute bar has `Open > High` (which occurs due to bid/ask spread rounding in the Dukascopy binary format), the aggregation window inherits an `Open` that exceeds the tracked `High`. Similarly, if the final minute bar's `Close` exceeds the running `High`, the emitted bar has `Close > High`.

Two confirmed bad bars in the sample file:
- `2026-02-23`: `Open=1.18333, High=1.18328` → `High < Open` by 0.5 pips
- `2026-03-31`: `Close=1.15743, High=1.15739` → `Close > High` by 0.4 pips

### Fix

Apply OHLC guards at two points in the loop:

**1. Window initialisation (first bar):**
```csharp
decimal open   = bars[i].Open;
decimal high   = Math.Max(bars[i].High, bars[i].Open);  // Open may exceed source High
decimal low    = Math.Min(bars[i].Low,  bars[i].Open);  // Open may fall below source Low
decimal close  = bars[i].Close;
decimal volume = bars[i].Volume;
var    timestamp = windowStart;
```

**2. Before emitting each window bar:**
```csharp
// Clamp close into [low, high] range
high  = Math.Max(high, close);
low   = Math.Min(low,  close);

result.Add(new BarRecord(symbol, interval, open, high, low, close, volume, timestamp));
```

This guarantees the output invariant `Low ≤ Open ≤ High` AND `Low ≤ Close ≤ High` for every emitted bar.

---

## 3. `ParseCandles` — Source Bar Sanity Filter

### Current Code

```csharp
if (o > 0 && h > 0 && l > 0 && c > 0)
    bars.Add(new BarRecord(symbol, "1m", o, h, l, c, (decimal)vol, ts));
```

### Enhancement

Extend the guard to also reject bars where `high < low` (malformed binary record):

```csharp
if (o > 0 && h > 0 && l > 0 && c > 0 && h >= l)
    bars.Add(new BarRecord(symbol, "1m", o, h, l, c, (decimal)vol, ts));
```

This prevents obviously corrupt records from entering the aggregation pipeline.

---

## 4. Test Class Structure — `DukascopyHelpersTests`

File location: `src/TradingResearchEngine.UnitTests/DukascopyHelpersTests.cs`

### Interval Parsing Tests

```csharp
[Theory]
[InlineData("1m",    1)]
[InlineData("5m",    5)]
[InlineData("15m",   15)]
[InlineData("30m",   30)]
[InlineData("1h",    60)]
[InlineData("1H",    60)]   // uppercase — previously fell through to 1440
[InlineData("60m",   60)]
[InlineData("4h",    240)]
[InlineData("4H",    240)]
[InlineData("1d",    1440)]
[InlineData("1D",    1440)]
[InlineData("daily", 1440)]
[InlineData("Daily", 1440)]
public void IntervalToMinutes_SupportedInterval_ReturnsCorrectMinutes(string interval, int expected)
    => Assert.Equal(expected, DukascopyHelpers.IntervalToMinutes(interval));

[Theory]
[InlineData("H1")]
[InlineData("hourly")]
[InlineData("1 hour")]
[InlineData("")]
[InlineData("bad")]
public void IntervalToMinutes_UnrecognizedInterval_ThrowsArgumentException(string interval)
    => Assert.Throws<ArgumentException>(() => DukascopyHelpers.IntervalToMinutes(interval));

[Fact]
public void IntervalToMinutes_NullInterval_ThrowsException()
    => Assert.ThrowsAny<Exception>(() => DukascopyHelpers.IntervalToMinutes(null!));
```

### OHLC Aggregation Tests

```csharp
// Helper: build a synthetic 1-minute bar
private static BarRecord Bar(decimal o, decimal h, decimal l, decimal c, DateTimeOffset ts)
    => new("EURUSD", "1m", o, h, l, c, 1000m, ts);

private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

[Fact]
public void Aggregate_FirstBarOpenExceedsHigh_OutputHighCoversOpen()
{
    // Source bar where Open > High (Dukascopy binary anomaly)
    var bars = new List<BarRecord> { Bar(1.1835m, 1.1832m, 1.1780m, 1.1800m, T0) };
    var result = DukascopyHelpers.Aggregate(bars, "1h", "EURUSD");
    Assert.Single(result);
    Assert.True(result[0].High >= result[0].Open,
        $"High ({result[0].High}) should be >= Open ({result[0].Open})");
}

[Fact]
public void Aggregate_LastBarCloseExceedsHigh_OutputHighCoversClose()
{
    var bars = new List<BarRecord>
    {
        Bar(1.1800m, 1.1850m, 1.1780m, 1.1820m, T0),
        Bar(1.1820m, 1.1830m, 1.1810m, 1.1840m, T0.AddMinutes(1)), // Close > first High
    };
    var result = DukascopyHelpers.Aggregate(bars, "1h", "EURUSD");
    Assert.Single(result);
    Assert.True(result[0].High >= result[0].Close,
        $"High ({result[0].High}) should be >= Close ({result[0].Close})");
}

[Fact]
public void Aggregate_FirstBarOpenBelowLow_OutputLowCoversOpen()
{
    var bars = new List<BarRecord> { Bar(1.1770m, 1.1850m, 1.1780m, 1.1800m, T0) };
    var result = DukascopyHelpers.Aggregate(bars, "1h", "EURUSD");
    Assert.Single(result);
    Assert.True(result[0].Low <= result[0].Open,
        $"Low ({result[0].Low}) should be <= Open ({result[0].Open})");
}

[Fact]
public void Aggregate_CleanInput_OhlcUnchanged()
{
    var bars = new List<BarRecord>
    {
        Bar(1.1800m, 1.1850m, 1.1780m, 1.1830m, T0),
        Bar(1.1830m, 1.1860m, 1.1820m, 1.1845m, T0.AddMinutes(1)),
    };
    var result = DukascopyHelpers.Aggregate(bars, "1h", "EURUSD");
    Assert.Single(result);
    Assert.Equal(1.1800m, result[0].Open);
    Assert.Equal(1.1860m, result[0].High);
    Assert.Equal(1.1780m, result[0].Low);
    Assert.Equal(1.1845m, result[0].Close);
}

[Fact]
public void Aggregate_EmptyInput_ReturnsEmpty()
{
    var result = DukascopyHelpers.Aggregate(new List<BarRecord>(), "1h", "EURUSD");
    Assert.Empty(result);
}
```

---

## 5. Files Changed

| File | Change |
|---|---|
| `src/TradingResearchEngine.Infrastructure/DataProviders/DukascopyHelpers.cs` | Fix `IntervalToMinutes` (remove `_ => 1440`, add throw); fix `Aggregate` (OHLC guards on window init and close); extend `ParseCandles` (`h >= l` guard) |
| `src/TradingResearchEngine.UnitTests/DukascopyHelpersTests.cs` | New file — `DukascopyHelpersTests` class with interval and OHLC tests |

No other files are modified. `DukascopyDataProvider.cs`, Core types, Application layer, and Web layer are untouched.
