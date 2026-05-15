# Samples

Reference scenario configurations and data for the TradingResearchEngine.

## Retained Directories

### `scenarios/`

JSON scenario configuration files demonstrating each built-in strategy. These use the
legacy flat `ScenarioConfig` format, which is automatically normalized to the canonical
V5+ sub-object shape at load time by `ScenarioConfigNormalizer`. They remain valid inputs
for the Web UI's "Load Scenario" flow and for integration tests.

### `data/`

Sample market data (SPY daily bars) used by the scenario configurations above. The CSV
format matches `CsvDataProvider` expectations and serves as a quick-start dataset for
new users.

## Removed

- `reports/sma-crossover-report.md` — Removed in PR Gate 6. The report used the obsolete
  `Equity Curve R²` metric (replaced by K-Ratio in V2) and did not match the current
  `MarkdownReporter` output format. Fresh reports are generated on demand via the Web UI.
