# Implementation Plan: Composite Strategy Engine

## Overview

This plan implements a runtime-configurable `CompositeStrategy` that evaluates declarative condition expressions against dynamically instantiated indicators. Tasks are ordered: Application-layer types/interfaces → Condition parser/compiler → Indicator factory/value provider → CompositeStrategy implementation → AI assistant updates → Export engine updates → DI registration → Unit tests → Integration tests → Final verification. Each task builds incrementally on previous work.

## Tasks

- [-] 1. Application layer — Core types and interfaces
  - [x] 1.1 Create `Application/Strategy/Composite/CompositeStrategyConfig.cs`
    - Define `CompositeStrategyConfig` immutable record with Name, Indicators (`IReadOnlyList<IndicatorConfig>`), EntryCondition, ExitCondition, DirectionMode (default Long)
    - Define `DirectionMode` enum: Long, Short, Both
    - Add System.Text.Json serialisation attributes for polymorphic dictionary support
    - Add XML doc comments on all public members
    - _Requirements: 2.1, 2.2, 2.3, 2.4_

  - [x] 1.2 Create `Application/Strategy/Composite/IndicatorConfig.cs`
    - Define `IndicatorConfig` immutable record with Id (string), Type (string), Parameters (`IReadOnlyDictionary<string, object>`)
    - Add XML doc comments
    - _Requirements: 2.2, 3.1_

  - [x] 1.3 Create `Application/Strategy/Composite/IIndicatorInstance.cs`
    - Define interface with Id, Type, IsWarm, Add(BarRecord), Reset(), CurrentValue, PreviousValue, GetSubValue(string), GetPreviousSubValue(string)
    - Add XML doc comments
    - _Requirements: 3.1, 7.1_

  - [x] 1.4 Create `Application/Strategy/Composite/Conditions/` AST node types
    - Create `ConditionNode` abstract record (base)
    - Create `LogicalNode(Left, Operator, Right)` sealed record
    - Create `ComparisonNode(Left, Operator, Right)` sealed record
    - Create `CrossNode(Left, Right, Direction)` sealed record
    - Create `ValueNode` abstract record (base)
    - Create `IndicatorRefNode(IndicatorId, SubProperty?)` sealed record
    - Create `PriceRefNode(Field)` sealed record
    - Create `LiteralNode(Value)` sealed record
    - Create enums: `LogicalOperator`, `ComparisonOperator`, `CrossDirection`, `PriceField`
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7, 5.1_

  - [x] 1.5 Create custom exception types
    - Create `ConditionParseException` with Position, Expected, Found properties
    - Create `ConditionValidationException` with UndefinedReferences, DefinedIndicatorIds properties
    - _Requirements: 5.2, 5.3_

- [ ] 2. Application layer — Condition expression parser and compiler
  - [x] 2.1 Create `Application/Strategy/Composite/Conditions/ConditionParser.cs`
    - Implement recursive-descent parser following the grammar: expression → logical_or → logical_and → primary → comparison | cross_call | parenthesised
    - Tokeniser: identifiers, numbers, operators, keywords (AND, OR, crosses_above, crosses_below)
    - Case-insensitive keywords, case-insensitive identifier matching
    - Throw `ConditionParseException` on syntax errors with position and expected tokens
    - _Requirements: 5.1, 5.2, 5.4_

  - [x] 2.2 Create `Application/Strategy/Composite/Conditions/ConditionValidator.cs`
    - Accept AST + list of defined indicator IDs
    - Walk AST and collect all `IndicatorRefNode` references
    - Throw `ConditionValidationException` if any reference is not in the defined IDs set
    - _Requirements: 5.3, 13.2_

  - [x] 2.3 Create `Application/Strategy/Composite/Conditions/ExpressionCompiler.cs`
    - Compile validated AST into `Func<IndicatorValueProvider, BarRecord, bool>` delegate
    - Use `System.Linq.Expressions` for zero-allocation per-bar evaluation
    - Implement short-circuit semantics for AND/OR (left-to-right, skip right when determined)
    - Handle null indicator values defensively (treat as non-triggering)
    - _Requirements: 6.1, 6.2, 6.3, 6.4_

  - [x] 2.4 Create `Application/Strategy/Composite/Conditions/ConditionPrettyPrinter.cs`
    - Walk AST and produce canonical condition expression string
    - Emit parentheses only where needed for precedence clarity
    - Used for round-trip validation and export
    - _Requirements: 5.5, 5.6_

- [ ] 3. Application layer — Indicator factory and value provider
  - [x] 3.1 Create `Application/Strategy/Composite/IndicatorFactory.cs`
    - Static `Create(IndicatorConfig)` method returning `IIndicatorInstance`
    - Support all 8 types: sma, ema, rsi, macd, bollinger, atr, stochastic, donchian
    - Map IndicatorConfig.Parameters to constructor arguments of existing `IIndicatorSeries<T>` wrappers
    - Throw `ArgumentException` for unknown types (list supported types in message)
    - Throw `ArgumentException` for missing required parameters (identify the parameter)
    - Apply defaults where indicator wrapper constructors define them
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5_

  - [x] 3.2 Create `Application/Strategy/Composite/IndicatorValueProvider.cs`
    - Dictionary mapping indicator IDs to current/previous values (case-insensitive)
    - `Update(IReadOnlyList<IIndicatorInstance>)` extracts current and previous values
    - `GetValue(string reference)` supports dot notation (e.g., "macd1.Signal")
    - `GetPreviousValue(string reference)` for cross-detection
    - `AllWarm` property returns true only when all indicators report IsWarm
    - Handle multi-value indicators (MACD, Bollinger, Stochastic) via sub-property keys
    - _Requirements: 7.1, 7.2, 7.3, 7.4_

  - [x] 3.3 Create concrete `IIndicatorInstance` adapter implementations
    - Create a generic `IndicatorInstanceAdapter<T>` wrapping existing `IIndicatorSeries<T>` wrappers
    - Implement value extraction for single-value indicators (SMA, EMA, RSI, ATR)
    - Implement sub-property extraction for multi-value indicators (MACD: Macd/Signal/Histogram, Bollinger: Upper/Middle/Lower, Stochastic: K/D, Donchian: Upper/Lower/Middle)
    - Track previous values for cross-detection
    - _Requirements: 3.1, 7.3_

- [ ] 4. Application layer — CompositeStrategy implementation
  - [x] 4.1 Create `Application/Strategy/Composite/CompositeStrategyConfigValidator.cs`
    - Static `Validate(CompositeStrategyConfig)` returning `IReadOnlyList<string>` of all violations
    - Check: indicator IDs unique, indicator types supported, entry/exit expressions parse, indicator IDs in expressions are defined
    - Return ALL violations, not just the first
    - _Requirements: 13.1, 13.2, 13.3, 13.4, 13.5_

  - [x] 4.2 Create `Application/Strategy/Composite/CompositeStrategy.cs`
    - Implement `IStrategy`, decorate with `[StrategyName("composite")]`
    - Constructor accepts `CompositeStrategyConfig`
    - At construction: validate config, instantiate indicators via IndicatorFactory, parse + validate + compile entry/exit expressions (fail-fast)
    - `OnMarketData`: feed bar to all indicators → update value provider → check AllWarm gate → evaluate compiled entry/exit delegates → emit SignalEvent based on state machine
    - Support DirectionMode (Long emits Long/Flat, Short emits Short/Flat, Both emits Long/Short/Flat)
    - No signals until all indicators warm
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 10.1, 10.2_

- [x] 5. Checkpoint — Core composite strategy compiles
  - Ensure `dotnet build` succeeds on Application project. Ask the user if questions arise.

- [ ] 6. Application layer — AI assistant updates
  - [x] 6.1 Extend `AIStrategyDraft` record with optional `CompositeConfig` property
    - Add `CompositeStrategyConfig? CompositeConfig = null` parameter
    - When StrategyType == "composite", CompositeConfig must be non-null
    - When StrategyType is a compiled type, CompositeConfig must be null
    - Ensure JSON round-trip with System.Text.Json
    - _Requirements: 9.1, 9.2, 9.3, 9.4_

  - [x] 6.2 Update `IAIStrategyAssistant` structured output schema
    - Add `compositeConfig` field to the Gemini structured output schema
    - Include CompositeStrategyConfig JSON schema (indicators, entryCondition, exitCondition, directionMode)
    - _Requirements: 8.1, 8.2_

  - [x] 6.3 Update `GeminiStrategyAssistant` implementation
    - Deserialise compositeConfig from AI response into AIStrategyDraft.CompositeConfig
    - When StrategyType == "composite": validate CompositeConfig via CompositeStrategyConfigValidator
    - If validation fails: retry once with correction prompt describing validation errors
    - If retry also fails: return draft with caveat
    - _Requirements: 8.2, 8.3, 8.5, 8.6_

  - [x] 6.4 Update system prompt (`Prompts/strategy-assistant-system.md`)
    - Document condition expression syntax with examples
    - Document available indicator types with their parameters and sub-properties
    - Document CompositeStrategyConfig JSON schema
    - Instruct AI to prefer composite output for novel strategies, compiled types for exact matches
    - _Requirements: 8.4, 12.3_

- [ ] 7. Infrastructure layer — Export engine updates
  - [x] 7.1 Create `Infrastructure/Export/CompositeExportHelper.cs`
    - Shared helper for translating composite configs to platform code
    - Indicator mapping: each of 8 types → MQL4/MQL5/PineScript equivalent calls
    - AST walker: translate ComparisonNode, LogicalNode, CrossNode, IndicatorRefNode, PriceRefNode, LiteralNode to platform syntax
    - CrossNode → previous-bar comparison pattern (MQL4/5) or `ta.crossover`/`ta.crossunder` (PineScript)
    - LogicalNode → `&&`/`||` (MQL4/5) or `and`/`or` (PineScript)
    - Emit `// NOTE:` comment and add Warning for unsupported constructs
    - _Requirements: 11.1, 11.2, 11.3, 11.4, 11.5_

  - [x] 7.2 Extend `MQL4StrategyExporter` with composite strategy handling
    - Detect StrategyType == "composite" and delegate to CompositeExportHelper
    - Generate OnInit() with indicator handle declarations
    - Generate OnTick() with entry/exit condition evaluation
    - _Requirements: 11.1, 11.2, 11.5_

  - [x] 7.3 Extend `MQL5StrategyExporter` with composite strategy handling
    - Same pattern as MQL4 but with CTrade class and MQL5 indicator functions
    - _Requirements: 11.1, 11.2, 11.5_

  - [x] 7.4 Extend `PineScriptExporter` with composite strategy handling
    - Use Pine Script v6 `ta.*` functions for indicators
    - Use `ta.crossover`/`ta.crossunder` for cross detection
    - Use `strategy.entry()`/`strategy.close()` for signals
    - _Requirements: 11.1, 11.2, 11.3, 11.5_

- [ ] 8. DI registration and wiring
  - [x] 8.1 Register CompositeStrategy in StrategyRegistry
    - Ensure `AddStrategyAssembly` picks up `[StrategyName("composite")]` on CompositeStrategy
    - Verify `StrategyRegistry.Resolve("composite")` returns `typeof(CompositeStrategy)`
    - _Requirements: 1.4, 10.1_

  - [x] 8.2 Update `RunScenarioUseCase` for composite strategy construction
    - When StrategyType == "composite": extract CompositeStrategyConfig from scenario config
    - Validate via CompositeStrategyConfigValidator before construction
    - Construct CompositeStrategy with the config
    - Return structured validation errors on failure
    - _Requirements: 10.1, 13.5_

  - [x] 8.3 Verify existing 6 compiled strategies remain unchanged
    - Ensure StrategyRegistry still resolves all 6 existing strategy names
    - No modifications to existing strategy constructors or behaviour
    - _Requirements: 12.1, 12.2, 12.4_

- [x] 9. Checkpoint — Full solution compiles
  - Ensure `dotnet build` succeeds on entire solution. Ask the user if questions arise.

- [ ] 10. Unit tests — Condition parser and compiler
  - [x] 10.1 Create `UnitTests/Strategy/Composite/ConditionParserTests.cs`
    - Test all comparison operators produce correct AST nodes
    - Test AND/OR logical operators combine sub-expressions correctly
    - Test parenthesised grouping overrides default precedence
    - Test `crosses_above` and `crosses_below` produce correct CrossNode
    - Test dot-notation indicator references (e.g., `macd1.Signal`)
    - Test numeric literals (positive, negative, decimal)
    - Test invalid expressions produce ConditionParseException with position info
    - Test unknown indicator references produce ConditionValidationException
    - _Requirements: 14.1, 14.2, 14.3, 14.5_

  - [x] 10.2 Create `UnitTests/Strategy/Composite/ConditionParserProperties.cs`
    - **Property 2: Condition Expression Parse Round-Trip**
    - `[Property(MaxTest = 20)]`
    - Generate arbitrary valid condition expression strings
    - Parse → pretty-print → re-parse produces equivalent AST
    - Tag: `// Feature: composite-strategy-engine, Property 2: Condition expression parse round-trip`
    - **Validates: Requirements 5.5, 5.6**

  - [x] 10.3 Create `UnitTests/Strategy/Composite/ConditionCompilerProperties.cs`
    - **Property 3: Compiled Expression Determinism**
    - `[Property(MaxTest = 20)]`
    - Generate arbitrary valid condition expressions and indicator value states
    - Compiled delegate evaluation matches direct AST interpretation
    - Tag: `// Feature: composite-strategy-engine, Property 3: Compiled expression determinism`
    - **Validates: Requirements 6.1, 6.2**

  - [x] 10.4 Write property test for short-circuit evaluation
    - **Property 6: Condition Evaluation Short-Circuit**
    - `[Property(MaxTest = 20)]`
    - AND with false left → right not evaluated
    - OR with true left → right not evaluated
    - Use side-effect tracking to verify non-evaluation
    - Tag: `// Feature: composite-strategy-engine, Property 6: Condition evaluation short-circuit`
    - **Validates: Requirement 6.4**

  - [x] 10.5 Write property test for crosses detection correctness
    - **Property 7: Crosses Detection Correctness**
    - `[Property(MaxTest = 20)]`
    - Generate pairs of indicator value sequences
    - `crosses_above(a, b)` true only when `a[current] > b[current] AND a[previous] <= b[previous]`
    - `crosses_below(a, b)` true only when `a[current] < b[current] AND a[previous] >= b[previous]`
    - Tag: `// Feature: composite-strategy-engine, Property 7: Crosses detection correctness`
    - **Validates: Requirements 4.6, 14.4**

- [ ] 11. Unit tests — Indicator factory and value provider
  - [x] 11.1 Create `UnitTests/Strategy/Composite/IndicatorFactoryTests.cs`
    - Test all 8 indicator types create successfully with valid parameters
    - Test unknown type throws ArgumentException listing supported types
    - Test missing required parameter throws ArgumentException identifying the parameter
    - Test default parameters applied when omitted
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5_

  - [x] 11.2 Create `UnitTests/Strategy/Composite/IndicatorFactoryProperties.cs`
    - **Property 4: Indicator Factory Completeness**
    - `[Property(MaxTest = 20)]`
    - For any indicator type in {sma, ema, rsi, macd, bollinger, atr, stochastic, donchian} with valid parameters, factory returns non-null instance that becomes warm after sufficient bars
    - Tag: `// Feature: composite-strategy-engine, Property 4: Indicator factory completeness`
    - **Validates: Requirements 3.1, 3.2**

  - [x] 11.3 Create `UnitTests/Strategy/Composite/IndicatorValueProviderTests.cs`
    - Test Update populates values from indicator instances
    - Test GetValue with dot notation for multi-value indicators
    - Test GetPreviousValue returns prior bar value
    - Test AllWarm returns false when any indicator not warm
    - Test AllWarm returns true when all indicators warm
    - Test unavailable (not warm) indicator returns null, not zero
    - _Requirements: 7.1, 7.2, 7.3, 7.4_

- [ ] 12. Unit tests — CompositeStrategy and config
  - [x] 12.1 Create `UnitTests/Strategy/Composite/CompositeStrategyTests.cs`
    - Test construction with valid config succeeds
    - Test construction with invalid config throws (unknown indicator type, bad expression)
    - Test OnMarketData emits no signal before all indicators warm
    - Test OnMarketData emits Long signal when entry condition met and not in position
    - Test OnMarketData emits Flat signal when exit condition met and in position
    - Test DirectionMode.Short emits Short/Flat signals
    - Test state machine: no duplicate entry signals, no exit when not in position
    - _Requirements: 1.1, 1.2, 1.3, 1.5, 1.6_

  - [x] 12.2 Create `UnitTests/Strategy/Composite/CompositeStrategyProperties.cs`
    - **Property 1: CompositeStrategyConfig JSON Round-Trip**
    - `[Property(MaxTest = 20)]`
    - Generate arbitrary valid CompositeStrategyConfig instances
    - Serialise to JSON → deserialise → assert semantic equivalence
    - Tag: `// Feature: composite-strategy-engine, Property 1: CompositeStrategyConfig JSON round-trip`
    - **Validates: Requirements 2.3, 2.4**

  - [x] 12.3 Write property test for signal equivalence
    - **Property 5: CompositeStrategy Signal Equivalence**
    - `[Property(MaxTest = 20)]`
    - Configure composite strategy to match SMA crossover logic
    - Feed same bar data to both composite and compiled MovingAverageCrossoverStrategy
    - Assert identical signal sequences after both warm
    - Tag: `// Feature: composite-strategy-engine, Property 5: CompositeStrategy signal equivalence`
    - **Validates: Requirements 10.3, 15.1**

  - [x] 12.4 Write property test for warm-up gating
    - **Property 8: Warm-Up Gating**
    - `[Property(MaxTest = 20)]`
    - Generate CompositeStrategyConfig with various indicator periods
    - Feed bars one at a time; assert no signals emitted until all indicators report IsWarm
    - Tag: `// Feature: composite-strategy-engine, Property 8: Warm-up gating`
    - **Validates: Requirement 1.6**

  - [x] 12.5 Create `UnitTests/Strategy/Composite/CompositeStrategyConfigValidatorTests.cs`
    - Test valid config returns empty error list
    - Test duplicate indicator IDs detected
    - Test unknown indicator type detected
    - Test undefined indicator reference in expression detected
    - Test unparseable expression detected
    - Test all violations returned (not just first)
    - _Requirements: 13.1, 13.2, 13.3, 13.4, 13.5_

- [x] 13. Checkpoint — Unit tests pass
  - Ensure all unit tests pass, ask the user if questions arise.

- [ ] 14. Integration tests
  - [x] 14.1 Create `IntegrationTests/Strategy/CompositeStrategyIntegrationTests.cs`
    - Test composite SMA crossover produces identical signals to compiled MovingAverageCrossoverStrategy on fixture CSV data
    - Test composite RSI-based entry/exit produces expected signals on known dataset
    - Test composite strategy produces deterministic results (same config + data → same result)
    - _Requirements: 15.1, 15.2, 15.3_

  - [x] 14.2 Create `IntegrationTests/Strategy/CompositeExportIntegrationTests.cs`
    - Test composite strategy export generates non-empty code for MQL4
    - Test composite strategy export generates non-empty code for MQL5
    - Test composite strategy export generates non-empty code for PineScript
    - Test export with crosses_above/crosses_below translates correctly per platform
    - _Requirements: 15.4_

- [x] 15. Final checkpoint — Full solution verification
  - Ensure all tests pass, run full solution build, verify no compiler warnings. Ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document (8 properties)
- Unit tests validate specific examples and edge cases
- The 8 supported indicator types: sma, ema, rsi, macd, bollinger, atr, stochastic, donchian
- All property tests use `[Property(MaxTest = 20)]` for faster execution as requested
- UnitTests reference Core and Application only — never Infrastructure
- IntegrationTests may reference all projects
- The existing 6 compiled strategies remain unchanged and fully functional
- CompositeStrategy slots into the existing engine pipeline with zero modifications to BacktestEngine, IRiskLayer, or IExecutionHandler
