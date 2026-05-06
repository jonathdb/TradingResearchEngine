# Requirements Document

## Introduction

The Composite Strategy Engine removes the constraint that the AI Strategy Assistant can only return one of 6 hardcoded strategy types. It introduces a `CompositeStrategy` class that can be configured at runtime with any combination of indicators from the existing Skender.Stock.Indicators wrapper library and rule-based entry/exit conditions expressed in a declarative condition language. The AI assistant outputs structured `CompositeStrategy` configurations instead of picking from a fixed set, while the 6 existing compiled strategies remain as optimised alternatives. Composite strategies are fully backtestable through the existing engine pipeline and exportable to MQL4/MQL5/PineScript.

## Glossary

- **Composite_Strategy**: An `IStrategy` implementation registered as `[StrategyName("composite")]` that evaluates declarative entry/exit rules against runtime-instantiated indicators.
- **Condition_Expression**: A declarative rule string combining indicator references, price references, comparisons, and logical operators (AND/OR) that evaluates to a boolean signal.
- **Condition_Evaluator**: Application-layer service that parses and evaluates Condition_Expression strings against current indicator and price values.
- **Indicator_Config**: A declarative specification of an indicator instance containing a type identifier, parameters, and a unique ID used for referencing in conditions.
- **Composite_Strategy_Config**: An immutable record containing a list of Indicator_Config entries, an entry Condition_Expression, an exit Condition_Expression, and optional metadata.
- **Indicator_Factory**: Application-layer service that instantiates `IIndicatorSeries` wrappers from an Indicator_Config specification.
- **Indicator_Value_Provider**: Runtime component that maintains current indicator values keyed by their string ID for use in condition evaluation.
- **Compiled_Expression**: A pre-compiled delegate representation of a Condition_Expression that avoids per-bar string parsing overhead.
- **Strategy_Exporter**: Existing Application-layer service extended to handle composite strategy export by translating the rule DSL to platform-specific code.
- **AI_Strategy_Assistant**: Existing Application-layer service updated to output CompositeStrategyConfig JSON in addition to the legacy hardcoded strategy types.

---

## Requirements

### Requirement 1: CompositeStrategy Class

**User Story:** As a strategy researcher, I want a single configurable strategy class that can express any combination of indicators and rules, so that I am not limited to 6 hardcoded strategy types.

#### Acceptance Criteria

1. THE Composite_Strategy SHALL implement `IStrategy` and be decorated with `[StrategyName("composite")]`.
2. THE Composite_Strategy SHALL accept a Composite_Strategy_Config at construction time containing indicator definitions, entry conditions, and exit conditions.
3. WHEN `OnMarketData` is called, THE Composite_Strategy SHALL feed the bar to all configured indicator instances, evaluate the entry and exit Condition_Expressions against current indicator values, and emit appropriate `SignalEvent` instances.
4. THE Composite_Strategy SHALL be discoverable by `StrategyRegistry` and resolvable via `StrategyRegistry.Resolve("composite")`.
5. THE Composite_Strategy SHALL support `DirectionMode` (Long, Short, Both) consistent with existing compiled strategies.
6. THE Composite_Strategy SHALL not emit signals until all configured indicators report `IsWarm == true`.

### Requirement 2: Composite Strategy Configuration Record

**User Story:** As a strategy researcher, I want a structured configuration format for composite strategies, so that the AI can output arbitrary strategy definitions as data.

#### Acceptance Criteria

1. THE Composite_Strategy_Config SHALL be an immutable record in the Application layer containing: Indicators (list of Indicator_Config), EntryCondition (string), ExitCondition (string), DirectionMode (default Long), and Name (string).
2. Each Indicator_Config SHALL contain: Id (string, unique within the config), Type (string matching a known indicator type), and Parameters (dictionary of string to object).
3. THE Composite_Strategy_Config SHALL be serialisable to and deserialisable from JSON using System.Text.Json without data loss.
4. FOR ALL valid Composite_Strategy_Config instances, serialising then deserialising SHALL produce an equivalent object (round-trip property).

### Requirement 3: Indicator Factory

**User Story:** As the composite strategy runtime, I want to instantiate indicator wrappers from declarative config, so that any supported indicator can be used without compile-time coupling.

#### Acceptance Criteria

1. THE Indicator_Factory SHALL accept an Indicator_Config and return an `IIndicatorSeries<T>` instance for the specified type.
2. THE Indicator_Factory SHALL support all 8 existing indicator types: sma, ema, rsi, macd, bollinger, atr, stochastic, donchian.
3. IF an unknown indicator type is specified, THEN THE Indicator_Factory SHALL throw a descriptive `ArgumentException` listing the supported types.
4. IF required parameters are missing or invalid, THEN THE Indicator_Factory SHALL throw a descriptive `ArgumentException` identifying the missing parameter.
5. THE Indicator_Factory SHALL apply default parameter values where the indicator wrapper constructor defines them and the config omits them.

### Requirement 4: Condition Expression Language

**User Story:** As a strategy researcher, I want a declarative rule language for entry/exit conditions, so that the AI can express arbitrary trading logic without generating compiled code.

#### Acceptance Criteria

1. THE Condition_Expression language SHALL support indicator value references by ID (e.g., `sma20`, `rsi14`).
2. THE Condition_Expression language SHALL support price references: `open`, `high`, `low`, `close`, `volume`.
3. THE Condition_Expression language SHALL support comparison operators: `>`, `<`, `>=`, `<=`, `==`, `!=`.
4. THE Condition_Expression language SHALL support logical operators: `AND`, `OR`, with parentheses for grouping.
5. THE Condition_Expression language SHALL support numeric literals for threshold comparisons (e.g., `rsi14 < 30`).
6. THE Condition_Expression language SHALL support cross-detection via a `crosses_above(a, b)` and `crosses_below(a, b)` function syntax.
7. THE Condition_Expression language SHALL support accessing sub-properties of multi-value indicators using dot notation (e.g., `macd1.Signal`, `bollinger1.Upper`, `stoch1.K`).

### Requirement 5: Condition Expression Parser

**User Story:** As the composite strategy runtime, I want to parse condition expressions into an evaluable representation, so that rule evaluation is correct and efficient.

#### Acceptance Criteria

1. THE Condition_Evaluator SHALL parse a Condition_Expression string into an abstract syntax tree (AST) representation.
2. IF the expression contains a syntax error, THEN THE Condition_Evaluator SHALL throw a descriptive parse exception identifying the error location and expected tokens.
3. IF the expression references an indicator ID not present in the strategy's Indicator_Config list, THEN THE Condition_Evaluator SHALL throw a validation exception identifying the unknown reference.
4. THE Condition_Evaluator SHALL validate expressions at strategy construction time (fail-fast), not on first bar evaluation.
5. THE Pretty_Printer SHALL format an AST back into a valid Condition_Expression string.
6. FOR ALL valid Condition_Expression strings, parsing then pretty-printing then parsing SHALL produce an equivalent AST (round-trip property).

### Requirement 6: Compiled Expression Evaluation

**User Story:** As a performance-conscious developer, I want condition expressions to be pre-compiled into delegates, so that per-bar evaluation avoids repeated string parsing and is efficient on the hot path.

#### Acceptance Criteria

1. THE Condition_Evaluator SHALL compile a parsed AST into a `Func<IndicatorValueProvider, BarRecord, bool>` delegate at strategy construction time.
2. WHEN `OnMarketData` is called, THE Composite_Strategy SHALL invoke the pre-compiled delegate rather than re-parsing the expression string.
3. THE compiled evaluation SHALL have no per-bar heap allocations beyond the indicator wrapper internals.
4. THE compiled evaluation SHALL short-circuit logical AND/OR operators (left-to-right evaluation, skip right operand when result is determined).

### Requirement 7: Indicator Value Provider

**User Story:** As the condition evaluator, I want a runtime context that provides current indicator values by ID, so that expressions can reference any configured indicator.

#### Acceptance Criteria

1. THE Indicator_Value_Provider SHALL maintain a dictionary mapping indicator IDs to their current numeric value(s).
2. THE Indicator_Value_Provider SHALL be updated after all indicators have processed the current bar, before condition evaluation.
3. WHEN an indicator produces a multi-value result (MACD, Bollinger, Stochastic), THE Indicator_Value_Provider SHALL expose each sub-value via dot-notation keys (e.g., `macd1.Macd`, `macd1.Signal`, `macd1.Histogram`).
4. WHEN an indicator is not yet warm, THE Indicator_Value_Provider SHALL report the indicator as unavailable rather than returning a stale or zero value.

### Requirement 8: AI Assistant Composite Output

**User Story:** As a strategy researcher, I want the AI assistant to output CompositeStrategyConfig JSON, so that it can create any strategy it wants rather than picking from 6 hardcoded types.

#### Acceptance Criteria

1. THE AI_Strategy_Assistant SHALL accept a new structured output schema that includes a `compositeConfig` field containing the full Composite_Strategy_Config.
2. WHEN the AI returns a composite configuration, THE AI_Strategy_Assistant SHALL set `StrategyType` to `"composite"` on the resulting AIStrategyDraft.
3. THE AI_Strategy_Assistant SHALL still support returning one of the 6 existing strategy types when the AI determines a compiled strategy is a better fit.
4. THE updated system prompt SHALL document the Condition_Expression syntax, available indicator types with their parameters, and the CompositeStrategyConfig JSON schema.
5. THE AI_Strategy_Assistant SHALL validate the returned CompositeStrategyConfig (indicator types exist, expressions parse, indicator IDs referenced in conditions are defined) before returning the draft.
6. IF validation fails, THEN THE AI_Strategy_Assistant SHALL retry once with a correction prompt describing the validation errors.

### Requirement 9: AIStrategyDraft Extension

**User Story:** As a developer, I want the AIStrategyDraft record to carry composite configuration data, so that the full strategy definition flows through the existing pipeline.

#### Acceptance Criteria

1. THE AIStrategyDraft record SHALL include an optional `CompositeConfig` property of type `CompositeStrategyConfig?` (null for non-composite drafts).
2. WHEN `StrategyType` is `"composite"`, THE AIStrategyDraft SHALL have a non-null `CompositeConfig`.
3. WHEN `StrategyType` is one of the 6 existing types, THE AIStrategyDraft SHALL have a null `CompositeConfig` and continue using the `Parameters` dictionary as before.
4. THE AIStrategyDraft SHALL remain serialisable to and deserialisable from JSON without data loss.

### Requirement 10: Composite Strategy Backtesting

**User Story:** As a strategy researcher, I want composite strategies to be backtestable through the existing engine pipeline, so that I can evaluate AI-generated strategies with the same rigour as compiled ones.

#### Acceptance Criteria

1. THE `RunScenarioUseCase` SHALL resolve `StrategyType = "composite"` via `StrategyRegistry` and construct the Composite_Strategy using the CompositeStrategyConfig from the scenario configuration.
2. THE Composite_Strategy SHALL work with the existing `IRiskLayer`, `IExecutionHandler`, `ISlippageModel`, and `ICommissionModel` pipeline without modification.
3. THE Composite_Strategy SHALL produce a `BacktestResult` with identical structure and metric computation as compiled strategies.
4. THE Composite_Strategy SHALL support all existing research workflows (parameter sweep, Monte Carlo, walk-forward) when parameters are expressed as overrides to the CompositeStrategyConfig.

### Requirement 11: Composite Strategy Export

**User Story:** As a strategy researcher, I want to export composite strategies to MQL4/MQL5/PineScript, so that AI-generated strategies can be deployed on external platforms.

#### Acceptance Criteria

1. WHEN a composite strategy is exported, THE Strategy_Exporter SHALL translate each Indicator_Config into the platform-equivalent indicator call (e.g., `iMA()` for MQL4, `ta.sma()` for PineScript).
2. WHEN a composite strategy is exported, THE Strategy_Exporter SHALL translate the entry and exit Condition_Expressions into platform-specific conditional logic.
3. THE Strategy_Exporter SHALL map `crosses_above` and `crosses_below` to the platform-appropriate crossover detection pattern.
4. WHEN an indicator type or expression construct has no direct platform equivalent, THE Strategy_Exporter SHALL emit a `// NOTE:` comment and add a Warning to ExportResult.
5. THE Strategy_Exporter SHALL generate syntactically valid code for all expressions composed of supported operators and indicator types.

### Requirement 12: Existing Strategy Preservation

**User Story:** As a strategy researcher, I want the 6 existing compiled strategies to remain available and unchanged, so that I retain optimised alternatives for known strategy patterns.

#### Acceptance Criteria

1. THE 6 existing compiled strategies SHALL remain registered in `StrategyRegistry` with their current names.
2. THE existing strategies SHALL produce identical backtest results to their pre-composite-engine behaviour.
3. THE AI_Strategy_Assistant SHALL prefer returning a compiled strategy type when the user's request maps cleanly to one of the 6 existing types.
4. THE `RunScenarioUseCase` SHALL continue to resolve and construct compiled strategies via their existing constructor parameter patterns.

### Requirement 13: Composite Strategy Configuration Validation

**User Story:** As a developer, I want composite configurations to be validated before engine execution, so that invalid configs fail fast with descriptive errors.

#### Acceptance Criteria

1. WHEN a CompositeStrategyConfig is submitted for backtesting, THE validation layer SHALL verify all indicator types are supported.
2. WHEN a CompositeStrategyConfig is submitted for backtesting, THE validation layer SHALL verify all indicator IDs referenced in conditions are defined in the indicators list.
3. WHEN a CompositeStrategyConfig is submitted for backtesting, THE validation layer SHALL verify entry and exit expressions parse without errors.
4. WHEN a CompositeStrategyConfig is submitted for backtesting, THE validation layer SHALL verify indicator IDs are unique within the config.
5. IF validation fails, THEN THE validation layer SHALL return a structured error listing all violations — not just the first one found.

### Requirement 14: Condition Expression Unit Tests

**User Story:** As a developer, I want comprehensive tests for the condition expression parser and evaluator, so that parsing correctness, evaluation semantics, and edge cases are verified.

#### Acceptance Criteria

1. THE unit tests SHALL verify that all comparison operators produce correct boolean results for known indicator/price inputs.
2. THE unit tests SHALL verify that AND/OR logical operators combine sub-expressions correctly.
3. THE unit tests SHALL verify that parenthesised grouping overrides default precedence.
4. THE unit tests SHALL verify that `crosses_above` and `crosses_below` detect transitions correctly (true only on the bar where the cross occurs).
5. THE unit tests SHALL verify that invalid expressions produce descriptive parse errors with location information.
6. THE unit tests SHALL include a round-trip property test: parse → pretty-print → parse produces an equivalent AST for all valid expressions.
7. THE unit tests SHALL include a property test: for any valid expression and any indicator state, evaluation produces a deterministic boolean without throwing.

### Requirement 15: Composite Strategy Integration Tests

**User Story:** As a developer, I want integration tests proving composite strategies produce correct signals and backtest results, so that end-to-end correctness is verified.

#### Acceptance Criteria

1. THE integration tests SHALL verify that a composite strategy configured as "sma fast > sma slow" produces identical signals to the compiled MovingAverageCrossoverStrategy on the same data.
2. THE integration tests SHALL verify that a composite strategy with RSI-based entry/exit produces expected signals on a known dataset.
3. THE integration tests SHALL verify that composite strategies produce deterministic results given the same config and data (same seed → same result).
4. THE integration tests SHALL verify that composite strategy export generates non-empty code for all 3 export formats.

---

## Out of Scope

- Custom indicator plugins (user-defined indicators beyond the 8 Skender wrappers) — deferred until plugin loader is implemented
- Visual condition builder UI (drag-and-drop rule composition) — potential future enhancement
- Genetic/evolutionary optimisation of composite strategy rules
- Multi-timeframe condition expressions (referencing indicators on different bar intervals)
- Stateful conditions (e.g., "was true N bars ago") beyond `crosses_above`/`crosses_below`
