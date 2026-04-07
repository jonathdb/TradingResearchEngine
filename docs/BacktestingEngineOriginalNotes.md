# Backtesting Engine — QuantStart Architecture Reference

## Origin

This engine is inspired by the QuantStart event-driven backtesting architecture, which models a trading system as a pipeline of loosely coupled components communicating through typed events.

## Core Concepts

- **Heartbeat loop**: an outer loop that advances one data step per iteration
- **Event queue**: a FIFO queue of typed events processed in the inner loop
- **Pipeline**: DataHandler → Strategy → RiskLayer → ExecutionHandler → Portfolio
- **Events**: MarketData, Signal, Order, Fill — each triggers the next stage

## Key Design Decisions from QuantStart

1. Events are the sole communication mechanism between components
2. The event queue is drained completely before the next heartbeat step
3. Portfolio state is updated only via FillEvents
4. Strategy receives market data and produces signals or orders
5. Execution is simulated with pluggable slippage and commission models

## Differences from Original

- C# records instead of Python classes
- Async/await throughout the pipeline
- Mandatory RiskLayer (not optional in original)
- Research workflows as first-class citizens
- Prop-firm evaluation as a bounded module
