---
trigger: save
fileMatch: "src/TradingResearchEngine.IntegrationTests/fixtures/**/*.csv"
---

# Strategy Fixture Validation

When a CSV fixture file is saved, validate that it contains the expected column headers:

For bar data: `Timestamp,Open,High,Low,Close,Volume`
For tick data: `Timestamp,BidPrice,BidSize,AskPrice,AskSize,LastPrice,LastVolume`

Flag any fixture file that doesn't match the expected schema.
