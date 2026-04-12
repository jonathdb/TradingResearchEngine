# V5 Migration Guide

This guide covers breaking-free migration from V4 flat config format to V5 sub-object format, new fields on existing records, deprecated patterns, and API versioning.

---

## Config Format Changes: Flat → Sub-Object

V5 decomposes `ScenarioConfig` into five typed sub-objects: `Data`, `Strategy`, `Risk`, `Execution`, and `Research`. The flat format continues to work — all changes are additive.

### Before (V4 Flat Format)

```json
{
  "ScenarioId": "donchian-breakout-spy",
  "Description": "20-day Donchian Channel Breakout on SPY daily.",
  "ReplayMode": "Bar",
  "DataProviderType": "csv",
  "DataProviderOptions": {
    "Symbol": "SPY",
    "Interval": "1D",
    "FilePath": "samples/data/spy-daily.csv"
  },
  "StrategyType": "donchian-breakout",
  "StrategyParameters": { "Period": 20 },
  "RiskParameters": {},
  "SlippageModelType": "ZeroSlippageModel",
  "CommissionModelType": "ZeroCommissionModel",
  "InitialCash": 100000,
  "AnnualRiskFreeRate": 0.05,
  "RandomSeed": null,
  "ResearchWorkflowType": null,
  "ResearchWorkflowOptions": null,
  "PropFirmOptions": null
}
```

### After (V5 Sub-Object Format — Preferred)

```json
{
  "ScenarioId": "donchian-breakout-spy",
  "Description": "20-day Donchian Channel Breakout on SPY daily.",
  "ReplayMode": "Bar",
  "Data": {
    "DataProviderType": "csv",
    "DataProviderOptions": {
      "Symbol": "SPY",
      "Interval": "1D",
      "FilePath": "samples/data/spy-daily.csv"
    },
    "Timeframe": "Daily",
    "BarsPerYear": 252
  },
  "Strategy": {
    "StrategyType": "donchian-breakout",
    "StrategyParameters": { "Period": 20 }
  },
  "Risk": {
    "RiskParameters": {},
    "InitialCash": 100000,
    "AnnualRiskFreeRate": 0.05
  },
  "Execution": {
    "SlippageModelType": "ZeroSlippageModel",
    "CommissionModelType": "ZeroCommissionModel",
    "FillMode": "NextBarOpen",
    "RealismProfile": "StandardBacktest"
  },
  "Research": {
    "ResearchWorkflowType": null,
    "ResearchWorkflowOptions": null,
    "RandomSeed": null
  },
  "PropFirmOptions": null
}
```

### Resolution Behavior

| Scenario | Behavior |
|----------|----------|
| V4 flat JSON (no sub-objects) | Works as before. `Effective*` properties construct sub-objects from top-level fields. |
| V5 sub-object JSON | Sub-objects take precedence. Top-level fields are ignored when sub-object is present. |
| Mixed (both present) | Sub-object wins. `PreflightValidator` emits a Warning with code `PRECEDENCE_CONFLICT`. |

Engine code always reads from `Effective*` computed properties, so both formats produce identical execution behavior.

---

## New Fields on Existing Records

All new fields are trailing parameters with defaults. Existing JSON files deserialize without modification.

### ScenarioConfig

| Field | Type | Default | Purpose |
|-------|------|---------|---------|
| `Data` | `DataConfig?` | `null` | V5 data provider sub-object |
| `Strategy` | `StrategyConfig?` | `null` | V5 strategy sub-object |
| `Risk` | `RiskConfig?` | `null` | V5 risk sub-object |
| `Execution` | `ExecutionConfig?` | `null` | V5 execution sub-object |
| `Research` | `ResearchConfig?` | `null` | V5 research sub-object |

### BacktestResult

| Field | Type | Default | Purpose |
|-------|------|---------|---------|
| `RealismAdvisories` | `IReadOnlyList<string>?` | `null` | Gap fills, volume warnings, session boundary fills |

### ExecutionOptions

| Field | Type | Default | Purpose |
|-------|------|---------|---------|
| `MaxFillPercentOfVolume` | `decimal?` | `null` | Cap fill quantity as fraction of bar volume |

### ExperimentMetadata

| Field | Type | Default | Purpose |
|-------|------|---------|---------|
| `PresetId` | `string?` | `null` | Preset used for the run |
| `DataFileIdentity` | `string?` | `null` | Data file hash for reproducibility |

### StrategyVersion

| Field | Type | Default | Purpose |
|-------|------|---------|---------|
| `SourceType` | `SourceType` | `Manual` | How the version was created (Template/Import/Fork/Manual) |
| `SourceTemplateId` | `string?` | `null` | Template ID when SourceType is Template |
| `SourceVersionId` | `string?` | `null` | Version ID when SourceType is Fork |
| `ImportedFrom` | `string?` | `null` | Original filename when SourceType is Import |
| `Hypothesis` | `string?` | `null` | User's hypothesis for the expected market edge |
| `ExpectedFailureMode` | `string?` | `null` | How the strategy is most likely to fail |

### StrategyTemplate

| Field | Type | Default | Purpose |
|-------|------|---------|---------|
| `FamilyPresets` | `Dictionary<string, Dictionary<string, object>>?` | `null` | Named parameter preset overrides |
| `DifficultyLevel` | `DifficultyLevel` | `Beginner` | Builder UX difficulty classification |

---

## Deprecated Patterns

### Flat ScenarioConfig Format

- **Status:** Deprecated in V5. Fully functional, will be removed in V6.
- **Detection:** API returns `X-Deprecation` header: `"Flat ScenarioConfig format is deprecated; use sub-object format. See /docs/migration."`
- **Action:** Migrate to sub-object format. The `Effective*` properties ensure identical behavior during the transition.
- **Timeline:** V5 — deprecated with header. V6 — flat format support removed.

### Direct Top-Level Field Access

Engine code should read from `Effective*` computed properties, not directly from top-level fields like `StrategyType`, `SlippageModelType`, etc. Direct access still works but bypasses sub-object precedence.

### Bare ScenarioConfig Body on Workflow Endpoints

- **Status:** Deprecated in V5.1. Fully functional, will be removed in V6.
- **Affected endpoints:** `POST /scenarios/sweep`, `POST /scenarios/montecarlo`, `POST /scenarios/walkforward`.
- **Detection:** API returns `X-Deprecation` header: `"Bare ScenarioConfig body is deprecated. Wrap in { \"Config\": ... }."`
- **Action:** Wrap the `ScenarioConfig` in a typed request wrapper that includes an optional `Options` object for workflow-specific settings.
- **New request format:**

```json
// POST /scenarios/montecarlo
{
  "Config": { /* ScenarioConfig */ },
  "Options": { "SimulationCount": 500, "Seed": 42, "BlockSize": 1 }
}

// POST /scenarios/sweep
{
  "Config": { /* ScenarioConfig */ },
  "Options": { "MaxDegreeOfParallelism": 4 }
}

// POST /scenarios/walkforward
{
  "Config": { /* ScenarioConfig */ },
  "Options": { "InSampleLength": "365.00:00:00", "OutOfSampleLength": "90.00:00:00" }
}
```

- **Wrapper types:** `SweepRequest`, `MonteCarloRequest`, `WalkForwardRequest` (defined in `TradingResearchEngine.Api.Dtos`).
- **Omitting `Options`:** When `Options` is null or absent, the endpoint uses default values for all workflow settings.
- **`POST /scenarios/run`** is unchanged and continues to accept a bare `ScenarioConfig` body.

---

## API Versioning Approach

V5 does not introduce URL-based API versioning. All changes are additive and backward-compatible:

- Existing endpoints (`POST /scenarios/run`, `/sweep`, `/montecarlo`, `/walkforward`) continue to work. Workflow endpoints now prefer typed request wrappers (`SweepRequest`, `MonteCarloRequest`, `WalkForwardRequest`) but still accept a bare `ScenarioConfig` body with an `X-Deprecation` header.
- New endpoints are added at new paths: `/jobs/*`, `/strategies/*`, `/workflows`, `/presets`, `/execution-models`, `/scenarios/resolve`.
- The `X-Deprecation` header signals deprecated patterns without breaking existing clients.
- Discovery endpoints include `SchemaVersion` (semver-like `"1.0"`) for programmatic schema evolution detection.

### Recommended Migration Steps

1. Update JSON payloads to use sub-object format (see before/after examples above).
2. Wrap workflow endpoint bodies in typed request wrappers (`SweepRequest`, `MonteCarloRequest`, `WalkForwardRequest`) to pass workflow-specific options and avoid the `X-Deprecation` header.
3. Adopt async job endpoints (`POST /jobs`) for long-running workflows instead of synchronous endpoints.
4. Use discovery endpoints to dynamically query available strategies, presets, and execution models.
5. Check for `X-Deprecation` headers in API responses and address flagged patterns.
