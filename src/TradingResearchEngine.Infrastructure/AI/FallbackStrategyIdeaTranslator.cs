using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingResearchEngine.Application.AI;
using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Application.Strategy;

namespace TradingResearchEngine.Infrastructure.AI;

/// <summary>
/// Wraps a configurable chain of LLM providers and attempts each in order,
/// returning the first successful result. When <c>EnableAIStrategyAssist</c>
/// is <c>false</c>, returns failure immediately without making any HTTP calls.
/// </summary>
public sealed class FallbackStrategyIdeaTranslator : IStrategyIdeaTranslator
{
    private readonly IReadOnlyList<IStrategyIdeaTranslator> _providers;
    private readonly LlmProviderOptions _options;
    private readonly ILogger<FallbackStrategyIdeaTranslator> _logger;

    /// <summary>
    /// Initialises the fallback translator with an ordered list of providers.
    /// </summary>
    /// <param name="providers">
    /// The ordered provider chain. The first provider that returns a successful result wins.
    /// </param>
    /// <param name="options">LLM provider configuration including the feature flag.</param>
    /// <param name="logger">Logger for recording provider failures.</param>
    public FallbackStrategyIdeaTranslator(
        IEnumerable<IStrategyIdeaTranslator> providers,
        IOptions<LlmProviderOptions> options,
        ILogger<FallbackStrategyIdeaTranslator> logger)
    {
        _providers = providers.ToList();
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<StrategyIdeaResult> TranslateAsync(
        string userDescription,
        IReadOnlyList<StrategyTemplate> templates,
        IReadOnlyList<StrategyParameterSchema> schemas,
        CancellationToken ct = default)
    {
        if (!_options.EnableAIStrategyAssist)
        {
            return new StrategyIdeaResult(false, FailureReason: "AI assist is disabled.");
        }

        foreach (var provider in _providers)
        {
            try
            {
                var result = await provider.TranslateAsync(userDescription, templates, schemas, ct);
                if (result.Success)
                {
                    _logger.LogInformation(
                        "Strategy translation succeeded via {Provider}",
                        provider.GetType().Name);
                    return result;
                }

                _logger.LogWarning(
                    "Provider {Provider} returned non-success: {Reason}",
                    provider.GetType().Name,
                    result.FailureReason);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Provider {Provider} threw an exception",
                    provider.GetType().Name);
            }
        }

        return new StrategyIdeaResult(false, FailureReason: "All LLM providers failed.");
    }
}
