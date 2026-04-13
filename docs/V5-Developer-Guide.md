# V5 Developer Guide

This guide covers the most common extension points in TradingResearchEngine V5: adding strategies, config presets, preflight rules, discovery endpoints, and the sub-object config format.

---

## Adding a New Strategy with `[ParameterMeta]` Attributes

### Step 1: Create the Strategy Class

Create a new file in `src/TradingResearchEngine.Application/Strategies/`. Your class must:

- Implement `IStrategy` (from `TradingResearchEngine.Core.Strategy`)
- Be decorated with `[StrategyName("your-strategy-name")]` (lowercase-kebab-case, unique)
- Annotate constructor parameters with `[ParameterMeta]`

```csharp
using TradingResearchEngine.Application.Strategy;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Strategy;

namespace TradingResearchEngine.Application.Strategies;

[StrategyName("rsi-mean-reversion")]
public sealed class RsiMeanReversionStrategy : IStrategy
{
    private readonly int _period;
    private readonly decimal _oversoldThreshold;
    private readonly decimal _overboughtThreshold;

    public RsiMeanReversionStrategy(
        [ParameterMeta(
            DisplayName = "RSI Period",
            Description = "Lookback period for RSI calculation.",
            SensitivityHint = SensitivityHint.High,
            Group = "Signal",
            DisplayOrder = 0,
            Min = 2, Max = 100)]
        int period = 14,

        [ParameterMeta(
            DisplayName = "Oversold Threshold",
            Description = "RSI level below which the asset is considered oversold.",
            SensitivityHint = SensitivityHint.Medium,
            Group = "Entry",
            DisplayOrder = 1,
            Min = 0, Max = 50)]
        decimal oversoldThreshold = 30m,

        [ParameterMeta(
            DisplayName = "Overbought Threshold",
            Description = "RSI level above which the position is closed.",
            SensitivityHint = SensitivityHint.Medium,
            Group = "Exit",
            DisplayOrder = 2,
            Min = 50, Max = 100)]
        decimal overboughtThreshold = 70m)
    {
        _period = period;
        _oversoldThreshold = oversoldThreshold;
        _overboughtThreshold = overboughtThreshold;
    }

    public IReadOnlyList<EngineEvent> OnMarketData(MarketDataEvent evt)
    {
        // Strategy logic here — emit SignalEvent with Direction.Long or Direction.Flat
        return Array.Empty<EngineEvent>();
    }
}
```

### `[ParameterMeta]` Attribute Reference

| Property | Type | Default | Purpose |
|----------|------|---------|---------|
| `DisplayName` | `string?` | Formatted from param name | Human-readable label |
| `Description` | `string?` | `""` | Help text for the builder UI |
| `SensitivityHint` | `SensitivityHint` | `Medium` | Overfitting sensitivity: `Low`, `Medium`, `High` |
| `Group` | `string` | `"Signal"` | Logical group: Signal, Entry, Exit, Risk, Filters, Execution |
| `IsAdvanced` | `bool` | `false` | Hidden in Simple mode when `true` |
| `DisplayOrder` | `int` | Parameter index | Sort order within group |
| `Min` | `object?` | `null` | Minimum allowed value (numeric only) |
| `Max` | `object?` | `null` | Maximum allowed value (numeric only) |

When `[ParameterMeta]` is absent, `StrategySchemaProvider` falls back to the constructor parameter name (formatted from camelCase), inferred type, and default value.

### Step 2: Register the Assembly

The strategy is auto-discovered at startup. In `Program.cs` of the Api or Cli host, the assembly containing your strategy must be registered:

```csharp
services.AddTradingResearchEngine(config)
        .AddStrategyAssembly(typeof(RsiMeanReversionStrategy).Assembly);
```

If your strategy lives in the same assembly as the existing built-in strategies (`TradingResearchEngine.Application`), no additional registration is needed — it's already registered.

### Step 3: Create a Template (Optional)

Add a `StrategyTemplate` in the template registration code to make your strategy available in the builder:

```csharp
new StrategyTemplate(
    TemplateId: "rsi-mean-reversion-template",
    Name: "RSI Mean Reversion",
    Description: "Buy oversold, sell overbought using RSI.",
    StrategyType: "rsi-mean-reversion",
    TypicalUseCase: "Range-bound markets with mean-reverting behavior.",
    DefaultParameters: new() { ["period"] = 14, ["oversoldThreshold"] = 30m, ["overboughtThreshold"] = 70m },
    RecommendedTimeframe: "Daily",
    DifficultyLevel: DifficultyLevel.Intermediate,
    FamilyPresets: new()
    {
        ["Conservative"] = new() { ["period"] = 21, ["oversoldThreshold"] = 25m },
        ["Aggressive"] = new() { ["period"] = 7, ["oversoldThreshold"] = 35m }
    })
```

### Step 4: Verify

Run the discovery endpoint to confirm your strategy is registered:

```
GET /strategies/rsi-mean-reversion/schema
```

This returns the full `StrategyParameterSchema` array built from your constructor and `[ParameterMeta]` attributes.

---

## Adding a Config Preset

Config presets are named, reusable sets of execution and risk defaults. Four built-in presets ship with V5 in `DefaultConfigPresets`.

### Built-In Presets

| Preset ID | Category | Slippage | Commission | Profile |
|-----------|----------|----------|------------|---------|
| `preset-fast-idea` | QuickCheck | Zero | Zero | FastResearch |
| `preset-standard` | Standard | FixedSpread | PerTrade | StandardBacktest |
| `preset-conservative` | Realistic | AtrScaled | PerShare | BrokerConservative |
| `preset-research-grade` | ResearchGrade | AtrScaled | PerShare | BrokerConservative |

### Creating a Custom Preset

Custom presets are persisted via `IRepository<ConfigPreset>` (JSON file storage). Create a `ConfigPreset` record:

```csharp
var myPreset = new ConfigPreset(
    PresetId: "preset-my-custom",
    Name: "My Custom Preset",
    Description: "Tailored for intraday H1 strategies with tight spreads.",
    Category: PresetCategory.Realistic,
    ExecutionConfig: new ExecutionConfig(
        SlippageModelType: "FixedSpreadSlippageModel",
        CommissionModelType: "PerShareCommissionModel",
        FillMode: FillMode.NextBarOpen,
        RealismProfile: ExecutionRealismProfile.StandardBacktest),
    RiskConfig: new RiskConfig(
        RiskParameters: new(),
        InitialCash: 50_000m,
        AnnualRiskFreeRate: 0.04m),
    IsBuiltIn: false);
```

Custom presets appear alongside built-in presets in `GET /presets` and in the builder Step 4 preset cards.

### Preset Application Precedence

When resolving configuration, the order is: **Default → Preset → Explicit**. Explicit values always win over preset values. The `ResolvedConfigService` annotates each value with its `ConfigProvenance` (Default, Preset, Explicit, Override).

---

## Extending the Preflight Validator

`PreflightValidator` (in `TradingResearchEngine.Application.Engine`) validates a `ScenarioConfig` before engine execution. It produces a `PreflightResult` containing `PreflightFinding` entries with severity levels: Error (blocks execution), Warning (proceeds with notice), and Recommendation (informational).

### Adding a New Validation Rule

1. Add a private method to `PreflightValidator`:

```csharp
private static void ValidateMyNewRule(ScenarioConfig config, List<PreflightFinding> findings)
{
    if (/* condition that should block execution */)
    {
        findings.Add(new PreflightFinding(
            Field: "MyField",
            Message: "MyField must be greater than zero.",
            Severity: PreflightSeverity.Error,
            Code: "MY_FIELD_INVALID"));
    }
}
```

2. Call it from the `Validate` method:

```csharp
public PreflightResult Validate(ScenarioConfig config)
{
    var findings = new List<PreflightFinding>();
    // ... existing validations ...
    ValidateMyNewRule(config, findings);
    return new PreflightResult(findings);
}
```

3. If the rule applies to a specific builder step, also call it from `ValidateAtStep` at the appropriate step number.

### Existing Validation Rules

| Code | Severity | Checks |
|------|----------|--------|
| `MISSING_PARAM` | Error | Required strategy parameter not provided |
| `RANGE_VIOLATION` | Error | Parameter outside `[Min, Max]` from schema |
| `TIMEFRAME_MISMATCH` | Warning | BarsPerYear doesn't match declared Timeframe |
| `SEALED_SET_CONFLICT` | Error | Execution window overlaps sealed test set |
| `INSUFFICIENT_DATA` | Warning | Bar count below MinBTL threshold |
| `PRECEDENCE_CONFLICT` | Warning | Both top-level and sub-object values present |

---

## Adding a Discovery Endpoint

Discovery endpoints live in `TradingResearchEngine.Api.Endpoints.DiscoveryEndpoints`. They use ASP.NET Core minimal API style.

### Example: Adding a New Discovery Endpoint

```csharp
// In DiscoveryEndpoints.cs, add to MapDiscoveryEndpoints:
app.MapGet("/my-resource", ListMyResource)
    .WithName("ListMyResource")
    .WithTags("Discovery")
    .Produces<IReadOnlyList<MyDto>>();

// Handler method:
private static IResult ListMyResource(/* inject dependencies */)
{
    var items = /* build response */;
    return Results.Ok(items);
}
```

Every discovery endpoint must have:
- `.WithName()` for OpenAPI operation ID
- `.WithTags("Discovery")` for grouping
- `.Produces<T>()` for response type annotation

### Existing Discovery Endpoints

| Method | Path | Returns |
|--------|------|---------|
| GET | `/strategies` | List of registered strategies with metadata |
| GET | `/strategies/{name}/schema` | Parameter schema for a specific strategy |
| GET | `/workflows` | Available research workflow types |
| GET | `/presets` | Built-in + custom config presets |
| GET | `/execution-models` | Slippage models, commission models, fill modes, realism profiles, session calendars, position sizing policies |

---

## Sub-Object Config Format

V5 decomposes `ScenarioConfig` into five typed sub-objects. The flat format is still supported but deprecated.

### Sub-Objects

| Sub-Object | Record Type | Namespace | Covers |
|------------|-------------|-----------|--------|
| `Data` | `DataConfig` | `Core.Configuration` | Provider type, options, timeframe, BarsPerYear |
| `Strategy` | `StrategyConfig` | `Core.Configuration` | Strategy type and parameters |
| `Risk` | `RiskConfig` | `Core.Configuration` | Risk parameters, initial cash, risk-free rate |
| `Execution` | `ExecutionConfig` | `Core.Configuration` | Slippage, commission, fill mode, realism profile, session options |
| `Research` | `ResearchConfig` | `Core.Configuration` | Workflow type, options, random seed, trace options |

### Resolution Rule

When a sub-object is present, it takes precedence over the equivalent top-level fields. The `Effective*` computed properties on `ScenarioConfig` handle this:

```csharp
// Sub-object wins when present; falls back to top-level fields
config.EffectiveDataConfig      // Data ?? new DataConfig(DataProviderType, ...)
config.EffectiveStrategyConfig  // Strategy ?? new StrategyConfig(StrategyType, ...)
config.EffectiveRiskConfig      // Risk ?? new RiskConfig(RiskParameters, ...)
config.EffectiveExecutionConfig // Execution ?? new ExecutionConfig(SlippageModelType, ...)
config.EffectiveResearchConfig  // Research ?? new ResearchConfig(ResearchWorkflowType, ...)
```

All engine code reads from `Effective*` properties, never directly from top-level fields. This ensures both formats produce identical behavior.
