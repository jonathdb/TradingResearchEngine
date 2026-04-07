using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Application.Research.Results;

/// <summary>Result of a variance testing workflow — one labelled result per preset.</summary>
public sealed record VarianceResult(
    IReadOnlyList<(string PresetName, BacktestResult Result)> Variants);
