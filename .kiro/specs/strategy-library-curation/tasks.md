# Implementation Plan — Strategy Library Curation & Research Alignment

- [x] 1. Add StrategyDescriptor and StrategyFamily to Application/Strategy

  - [x] 1.1 Create `StrategyDescriptor` record in Application/Strategy/StrategyDescriptor.cs


    - Fields: StrategyType, DisplayName, Family, Description, Hypothesis, BestFor (nullable), SuggestedStudies (nullable string array)


    - _Requirements: 5.1_


  - [x] 1.2 Create `StrategyFamily` static class in Application/Strategy/StrategyFamily.cs


    - String constants: Trend, MeanReversion, Breakout, RegimeAware, Benchmark

    - _Requirements: 5.2_


  - [ ] 1.3 Add optional `Descriptor` trailing parameter to `StrategyTemplate` record
    - `StrategyDescriptor? Descriptor = null` — backwards-compatible
    - _Requirements: 5.3_
  - [ ] 1.4 Write unit tests for StrategyDescriptor
    - Verify all 6 descriptors are populated, lookup by StrategyType, null fallback for unknown types


    - _Requirements: 5.1, 5.4, 5.6_

- [ ] 2. Implement new strategy classes
  - [x] 2.1 Create `VolatilityScaledTrendStrategy` in Application/Strategies


    - O(1) rolling SMA accumulators for fast/slow, Wilder-smoothed ATR
    - Entry: fastSMA > slowSMA → Long with Strength = Close / ATR
    - Exit: fastSMA <= slowSMA → Flat
    - Register with `[StrategyName("volatility-scaled-trend")]`
    - Parameters: fastPeriod (10), slowPeriod (50), atrPeriod (14)
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 2.8_


  - [ ] 2.2 Write unit tests for VolatilityScaledTrendStrategy
    - Warmup behavior (no signals before max(slowPeriod, atrPeriod) bars)


    - Entry signal with correct Strength = Close / ATR
    - Exit signal on crossover reversal
    - No duplicate signals when already in correct state


    - _Requirements: 10.1, 10.2, 10.3_
  - [ ] 2.3 Create `ZScoreMeanReversionStrategy` in Application/Strategies
    - O(1) rolling sum and sum-of-squares accumulators

    - z = (Close - SMA) / StdDev


    - Entry: z < -entryThreshold → Long


    - Exit: z > exitThreshold → Flat


    - Register with `[StrategyName("zscore-mean-reversion")]`
    - Parameters: lookback (30), entryThreshold (2.0m), exitThreshold (0.0m)


    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6_

  - [x] 2.4 Write unit tests for ZScoreMeanReversionStrategy


    - Warmup behavior, entry at z < -threshold, exit at z > exitThreshold


    - No signal when already in correct state
    - _Requirements: 10.1, 10.2, 10.5_


  - [x] 2.5 Create `BaselineBuyAndHoldStrategy` in Application/Strategies

    - Emit Direction.Long with Strength = 1.0m on bar warmupBars, never exit


    - Register with `[StrategyName("baseline-buy-and-hold")]`
    - Parameters: warmupBars (1)


    - _Requirements: 4.1, 4.2, 4.3, 4.4_
  - [ ] 2.6 Write unit tests for BaselineBuyAndHoldStrategy
    - Exactly one Long signal after warmup with Strength = 1.0m
    - No Flat signal ever emitted

    - No further signals after initial entry


    - _Requirements: 10.1, 10.2, 10.4_



- [ ] 3. Remove redundant strategy classes and update templates
  - [x] 3.1 Delete removed strategy files from Application/Strategies


    - Remove: SmaCrossoverStrategy.cs, BollingerBandsStrategy.cs, MeanReversionStrategy.cs, BreakoutStrategy.cs, RsiStrategy.cs


    - _Requirements: 1.2_
  - [ ] 3.2 Delete corresponding test files from UnitTests
    - Remove any test classes for the 5 deleted strategies
    - _Requirements: 10.6_
  - [x] 3.3 Update `DefaultStrategyTemplates.All` to contain exactly 6 templates with full descriptors


    - Replace the existing 6 templates with the curated set including StrategyDescriptor on each
    - Kept strategies (stationary-mean-reversion, macro-regime-rotation, donchian-breakout) retain their existing type keys — only descriptor/display framing changes
    - _Requirements: 1.4, 5.4, 8.1, 8.2, 8.3_
  - [ ] 3.4 Update any references to removed strategies in docs, tests, or other code
    - Search for removed strategy type names and update or remove references
    - _Requirements: 1.6_

- [ ] 4. Replace sample scenario files
  - [ ] 4.1 Delete removed sample scenario files
    - Remove: sma-crossover-spy.json, sma-crossover-dukascopy.json, sma-crossover-yahoo.json, bollinger-bands-spy.json
    - _Requirements: 9.4_
  - [ ] 4.2 Create new sample scenario files
    - Add: volatility-scaled-trend-spy.json, zscore-meanrev-spy.json, baseline-buy-and-hold-spy.json
    - Each uses csv provider, spy-daily.csv, default parameters from template
    - _Requirements: 9.6, 9.2, 9.3_
  - [ ] 4.3 Update kept sample scenario files with revised descriptions
    - Update descriptions in: donchian-breakout-spy.json, stationary-meanrev-spy.json, macro-regime-spy.json
    - _Requirements: 9.5_

- [ ] 5. Amend Strategy Builder UX
  - [ ] 5.1 Enhance template cards in StrategyBuilder.razor Step 1
    - Add family badge (colored MudChip), hypothesis text (italic), from StrategyDescriptor
    - If Descriptor is null, render current layout unchanged
    - _Requirements: 6.1, 6.2_
  - [ ] 5.2 Add hypothesis prefill to StrategyBuilder.razor Step 5
    - Pre-fill hypothesis from Descriptor on new creation only
    - Add editable hypothesis text field to Review step
    - Pass hypothesis to StrategyIdentity on save
    - Do not overwrite existing hypothesis on clone/edit flows
    - _Requirements: 6.3_

- [ ] 6. Amend Strategy Detail UX
  - [ ] 6.1 Add family badge to StrategyDetail.razor header band
    - Lookup StrategyDescriptor from DefaultStrategyTemplates by StrategyType
    - Show family chip next to strategy type chip; skip if no descriptor found
    - _Requirements: 7.1_
  - [ ] 6.2 Add suggested study indicators to StrategyDetail.razor Research tab
    - Show star icon or "Recommended" label on study buttons matching SuggestedStudies
    - If SuggestedStudies is null or empty, render all buttons without markers
    - _Requirements: 7.2, 7.3_

- [ ] 7. Catalog sanity check
  - [ ] 7.1 Write a verification test that validates the curated catalog
    - Exactly 6 templates in DefaultStrategyTemplates.All
    - Each template has a non-null StrategyDescriptor
    - All 6 strategy types resolve via StrategyRegistry without error
    - Descriptor lookup for a non-existent type returns null (fallback behavior)
    - Removed strategy types (sma-crossover, bollinger-bands, mean-reversion, breakout, rsi) throw StrategyNotFoundException
    - _Requirements: 1.1, 5.6, 11.3_
  - [ ] 7.2 Verify exactly 6 sample scenario files exist in samples/scenarios
    - Manual or scripted check that the directory contains the expected 6 files
    - _Requirements: 9.1_
