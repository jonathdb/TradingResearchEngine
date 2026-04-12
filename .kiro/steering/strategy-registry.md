# Strategy Registry Pattern

## Overview

Strategies are discovered at startup via a named registry rather than hard-coded DI registrations.
This allows new strategies to be added without modifying host configuration.

## Attribute

Every `IStrategy` implementation must be decorated with `[StrategyName]`:

```csharp
[StrategyName("moving-average-crossover")]
public sealed class MovingAverageCrossoverStrategy : IStrategy { ... }
```

- The name must be lowercase-kebab-case.
- Names must be unique across all registered assemblies.
- Duplicate names at startup throw `InvalidOperationException` identifying both conflicting types.

## StrategyRegistry (Application layer)

```csharp
public sealed class StrategyRegistry
{
    public void RegisterAssembly(Assembly assembly);
    public Type Resolve(string strategyName);   // throws if not found
    public IReadOnlyList<string> KnownNames { get; }
}
```

- `RegisterAssembly` scans the assembly for all non-abstract types implementing `IStrategy`
  decorated with `[StrategyName]`.
- `Resolve` throws `StrategyNotFoundException` (Application layer) with the requested name
  and the list of `KnownNames` when the name is not found.
- `StrategyRegistry` is registered as a singleton in DI.

## Registration at Startup

In `Program.cs` of Cli and Api:

```csharp
services.AddTradingResearchEngine(config)
        .AddStrategyAssembly(typeof(MovingAverageCrossoverStrategy).Assembly);
```

`AddStrategyAssembly` is an extension on `IServiceCollection` defined in Application's
`ServiceCollectionExtensions.cs`. It calls `StrategyRegistry.RegisterAssembly` on the
singleton instance.

## Resolution in RunScenarioUseCase

`RunScenarioUseCase` resolves the strategy type from `ScenarioConfig.StrategyType` via
`StrategyRegistry.Resolve` before constructing the engine. A `StrategyNotFoundException`
is treated as a validation error (structured response, no run started).

## Upgrade Path to Plugin Loader

When full plugin isolation is needed:

1. Replace `RegisterAssembly(Assembly)` with `RegisterPlugin(string dllPath)` that uses
   `AssemblyLoadContext` to load the assembly in isolation.
2. Validate the loaded assembly against a configurable allow-list before scanning.
3. The `IStrategy` contract and `[StrategyName]` attribute remain unchanged.
4. `RunScenarioUseCase` requires no changes.

## Scope

- `IStrategy` implementations in UnitTests and IntegrationTests do not need `[StrategyName]`
  — they are constructed directly in tests, not resolved via the registry.

## Strategy Responsibility Boundaries

Strategies must remain focused on signal generation. They must not embed:
- Market-hours or session logic (handled by `ISessionCalendar` and engine session filtering)
- Execution cost modelling (handled by `ISlippageModel` and `ICommissionModel`)
- Position sizing logic (handled by `IPositionSizingPolicy`)
- Report generation or analytics concerns

Strategies emit `Direction.Long` to enter and `Direction.Flat` to exit.
V5 adds `Direction.Short` for exhaustive switch coverage; runtime short-selling is guarded by `LongOnlyGuard` (V6 task).
