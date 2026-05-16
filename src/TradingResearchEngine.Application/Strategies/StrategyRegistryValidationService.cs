using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TradingResearchEngine.Application.Strategies;

/// <summary>
/// Hosted service that verifies all registered strategies can be instantiated at application startup.
/// Calls <see cref="StrategyRegistry.VerifyAll"/> and logs results. Runs once during startup
/// and then completes. Failures are logged as warnings but do not prevent application startup.
/// </summary>
public sealed class StrategyRegistryValidationService : IHostedService
{
    private readonly StrategyRegistry _registry;
    private readonly ILogger<StrategyRegistryValidationService> _logger;

    /// <summary>
    /// Creates a new instance of the strategy registry validation service.
    /// </summary>
    /// <param name="registry">The strategy registry to verify.</param>
    /// <param name="logger">Logger for reporting validation results.</param>
    public StrategyRegistryValidationService(
        StrategyRegistry registry,
        ILogger<StrategyRegistryValidationService> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting strategy registry verification...");

        var result = _registry.VerifyAll(_logger);

        if (result.AllSucceeded)
        {
            _logger.LogInformation(
                "Strategy registry verification complete: all {Count} strategies verified successfully.",
                result.TotalRegistered);
        }
        else
        {
            _logger.LogWarning(
                "Strategy registry verification complete: {FailureCount}/{TotalCount} strategies failed verification.",
                result.FailureCount, result.TotalRegistered);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
