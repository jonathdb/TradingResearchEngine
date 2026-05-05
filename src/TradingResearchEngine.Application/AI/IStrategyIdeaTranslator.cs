using TradingResearchEngine.Application.Strategy;

namespace TradingResearchEngine.Application.AI;

/// <summary>
/// Translates a natural-language strategy description into a structured
/// <see cref="StrategyIdeaResult"/> by leveraging an LLM provider.
/// </summary>
/// <remarks>
/// <para>
/// Implementations may call external LLM APIs (GoogleAIStudio, Groq, Ollama)
/// or return a disabled result when <c>EnableAIStrategyAssist</c> is <c>false</c>.
/// </para>
/// <para>
/// The <see cref="TranslateAsync"/> method accepts the full set of available templates
/// and parameter schemas so the LLM can select the most appropriate template and
/// suggest valid parameter values within schema bounds.
/// </para>
/// </remarks>
public interface IStrategyIdeaTranslator
{
    /// <summary>
    /// Translates a user's natural-language strategy description into a structured result
    /// containing a selected template, suggested parameters, hypothesis, and timeframe.
    /// </summary>
    /// <param name="userDescription">
    /// The user's plain-language description of their trading idea.
    /// </param>
    /// <param name="templates">
    /// The available strategy templates the LLM may select from.
    /// </param>
    /// <param name="schemas">
    /// The parameter schemas for all available strategies, used to constrain suggested values.
    /// </param>
    /// <param name="ct">Cancellation token for cooperative cancellation.</param>
    /// <returns>
    /// A <see cref="StrategyIdeaResult"/> indicating success or failure with suggested configuration.
    /// </returns>
    Task<StrategyIdeaResult> TranslateAsync(
        string userDescription,
        IReadOnlyList<StrategyTemplate> templates,
        IReadOnlyList<StrategyParameterSchema> schemas,
        CancellationToken ct = default);
}
