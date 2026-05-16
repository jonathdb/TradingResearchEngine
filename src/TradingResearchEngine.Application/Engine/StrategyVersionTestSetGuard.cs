using TradingResearchEngine.Application.Strategies;

namespace TradingResearchEngine.Application.Engine;

/// <summary>
/// Default implementation of <see cref="ITestSetGuard"/> that determines test set consumption
/// by checking whether the strategy has already reached <see cref="DevelopmentStage.FinalTest"/>.
/// </summary>
public sealed class StrategyVersionTestSetGuard : ITestSetGuard
{
    private readonly IStrategyRepository _strategyRepo;

    /// <summary>
    /// Creates a new <see cref="StrategyVersionTestSetGuard"/>.
    /// </summary>
    /// <param name="strategyRepo">The strategy repository for looking up version and strategy state.</param>
    public StrategyVersionTestSetGuard(IStrategyRepository strategyRepo)
    {
        _strategyRepo = strategyRepo;
    }

    /// <inheritdoc/>
    public async Task<bool> IsConsumedAsync(string strategyVersionId, CancellationToken ct = default)
    {
        var version = await _strategyRepo.GetVersionAsync(strategyVersionId, ct);
        if (version is null)
            return false;

        var strategy = await _strategyRepo.GetAsync(version.StrategyId, ct);
        if (strategy is null)
            return false;

        return strategy.Stage == DevelopmentStage.FinalTest;
    }

    /// <inheritdoc/>
    public async Task MarkConsumedAsync(string strategyVersionId, CancellationToken ct = default)
    {
        var version = await _strategyRepo.GetVersionAsync(strategyVersionId, ct);
        if (version is null)
            return;

        var strategy = await _strategyRepo.GetAsync(version.StrategyId, ct);
        if (strategy is null)
            return;

        var updated = strategy with { Stage = DevelopmentStage.FinalTest };
        await _strategyRepo.SaveAsync(updated, ct);
    }
}
