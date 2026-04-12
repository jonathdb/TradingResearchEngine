# Event-Driven Architecture Notes

## Event Hierarchy

```
EngineEvent (abstract record)
├── MarketDataEvent (abstract)
│   ├── BarEvent (OHLCV + timestamp)
│   └── TickEvent (bid/ask levels + last trade)
├── SignalEvent (direction + optional strength)
├── OrderEvent (direction + quantity + type + RiskApproved flag + StopPrice + MaxBarsPending + StopTriggered)
└── FillEvent (fill price + commission + slippage + ExecutionOutcome + RemainingQuantity + RejectionReason)

> **V5 Note:** `Direction` is now `{ Long, Short, Flat }`. `Short` is defined for exhaustive switch coverage but guarded at runtime by `LongOnlyGuard` — short-selling execution is deferred to V6.
```

All events carry a `DateTimeOffset Timestamp`. Events are immutable records.

## EventQueue

- Backed by `ConcurrentQueue<EngineEvent>` for thread safety
- One queue per engine run — never shared across runs
- FIFO ordering within a single heartbeat step
- `TryDequeue` returns false when empty, signalling the inner loop to exit

## Replay Modes

- `Bar`: DataHandler emits one `BarEvent` per heartbeat step
- `Tick`: DataHandler emits one `TickEvent` per heartbeat step

Both modes use the identical heartbeat-loop and dispatch architecture. The only difference is the event subtype and the `IDataProvider` method called.

## Value Types

- `BidLevel`, `AskLevel`: readonly record structs for order book depth
- `LastTrade`: readonly record struct for most recent trade
- `Position`, `ClosedTrade`: sealed records for portfolio state
- `EquityCurvePoint`: sealed record for portfolio snapshots — includes `TotalEquity`, `CashBalance`, `UnrealisedPnl`, `RealisedPnl`, and `OpenPositionCount`; appended by `Portfolio.MarkToMarket`, not by fill processing
- `ProgressUpdate`: sealed record for workflow progress reporting (`CurrentStep`, `TotalSteps`, `Message`, computed `Fraction`)
- `ExecutionOutcome`: enum on `FillEvent` — `Filled`, `PartiallyFilled`, `Unfilled`, `Rejected`, `Expired`; defaults to `Filled` for backward compatibility
- `TradingSession`: readonly record struct (`Name`, `Start`, `End`, `TimeZoneId`) — used by `ISessionCalendar` implementations to define tradable windows
