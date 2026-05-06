using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mscc.GenerativeAI;
using TradingResearchEngine.Application.AI;
using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Application.Strategy;
using TradingResearchEngine.Application.Strategy.Composite;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Infrastructure.AI;

/// <summary>
/// Google Gemini implementation of <see cref="IAIStrategyAssistant"/>.
/// Uses structured JSON output mode for reliable parsing.
/// Retries once on unknown StrategyType with a correction prompt containing KnownNames.
/// </summary>
public sealed class GeminiStrategyAssistant : IAIStrategyAssistant
{
    private readonly GeminiOptions _options;
    private readonly StrategyRegistry _registry;
    private readonly ILogger<GeminiStrategyAssistant> _logger;
    private readonly IGeminiClient _geminiClient;

    /// <summary>
    /// Initializes a new instance of <see cref="GeminiStrategyAssistant"/>.
    /// </summary>
    /// <param name="options">Gemini configuration options.</param>
    /// <param name="registry">Strategy registry for validating strategy types.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="geminiClient">Abstracted Gemini client for testability.</param>
    public GeminiStrategyAssistant(
        IOptions<GeminiOptions> options,
        StrategyRegistry registry,
        ILogger<GeminiStrategyAssistant> logger,
        IGeminiClient geminiClient)
    {
        _options = options.Value;
        _registry = registry;
        _logger = logger;
        _geminiClient = geminiClient;

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException(
                "Gemini API key is not configured. Set GeminiOptions.ApiKey to enable AI strategy assistant features.");
        }
    }

    /// <inheritdoc/>
    public async Task<AIStrategyDraft> GenerateStrategyAsync(string prompt, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var systemPrompt = await LoadSystemPromptAsync(ct);
        var userMessage = $"Generate a trading strategy based on the following description:\n\n{prompt}";

        var json = await _geminiClient.GenerateJsonAsync(systemPrompt, userMessage, ct);
        var draft = DeserializeDraft(json);

        // Validate StrategyType against known names
        if (!IsKnownStrategyType(draft.StrategyType))
        {
            _logger.LogWarning("Unknown StrategyType '{StrategyType}' returned by AI. Retrying with correction prompt.", draft.StrategyType);

            var correctionPrompt = BuildCorrectionPrompt(draft.StrategyType);
            var retryJson = await _geminiClient.GenerateJsonAsync(systemPrompt, correctionPrompt, ct);
            var retryDraft = DeserializeDraft(retryJson);

            if (!IsKnownStrategyType(retryDraft.StrategyType))
            {
                _logger.LogWarning("Retry also returned unknown StrategyType '{StrategyType}'. Adding caveat.", retryDraft.StrategyType);
                var caveats = retryDraft.Caveats.ToList();
                caveats.Add($"Unrecognised strategy type '{retryDraft.StrategyType}'. Known types: {string.Join(", ", _registry.KnownNames)}");
                return retryDraft with
                {
                    Caveats = caveats,
                    SourceType = SourceType.AIGenerated
                };
            }

            return retryDraft with { SourceType = SourceType.AIGenerated };
        }

        // Validate CompositeConfig when StrategyType is "composite"
        if (string.Equals(draft.StrategyType, "composite", StringComparison.OrdinalIgnoreCase)
            && draft.CompositeConfig is not null)
        {
            var validationErrors = CompositeStrategyConfigValidator.Validate(draft.CompositeConfig);
            if (validationErrors.Count > 0)
            {
                _logger.LogWarning(
                    "CompositeConfig validation failed with {ErrorCount} error(s). Retrying with correction prompt.",
                    validationErrors.Count);

                var correctionPrompt = BuildCompositeValidationCorrectionPrompt(draft, validationErrors);
                var retryJson = await _geminiClient.GenerateJsonAsync(systemPrompt, correctionPrompt, ct);
                var retryDraft = DeserializeDraft(retryJson);

                // Re-validate the retry result
                if (string.Equals(retryDraft.StrategyType, "composite", StringComparison.OrdinalIgnoreCase)
                    && retryDraft.CompositeConfig is not null)
                {
                    var retryErrors = CompositeStrategyConfigValidator.Validate(retryDraft.CompositeConfig);
                    if (retryErrors.Count > 0)
                    {
                        _logger.LogWarning(
                            "Retry also failed CompositeConfig validation with {ErrorCount} error(s). Returning draft with caveats.",
                            retryErrors.Count);

                        var caveats = retryDraft.Caveats.ToList();
                        caveats.Add($"Composite configuration has validation errors: {string.Join("; ", retryErrors)}");
                        return retryDraft with
                        {
                            Caveats = caveats,
                            SourceType = SourceType.AIGenerated
                        };
                    }
                }

                return retryDraft with { SourceType = SourceType.AIGenerated };
            }
        }

        return draft with { SourceType = SourceType.AIGenerated };
    }

    /// <inheritdoc/>
    public async Task<AIStrategyDraft> RefineStrategyAsync(
        AIStrategyDraft current,
        BacktestResult lastResult,
        string refinementPrompt,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var systemPrompt = await LoadSystemPromptAsync(ct);
        var metricsContext = BuildMetricsContext(lastResult);
        var userMessage = $"""
            Refine the following strategy based on backtest results and user feedback.

            Current Strategy:
            - Name: {current.StrategyName}
            - Type: {current.StrategyType}
            - Hypothesis: {current.Hypothesis}
            - Parameters: {JsonSerializer.Serialize(current.Parameters)}

            Backtest Metrics:
            {metricsContext}

            User Feedback:
            {refinementPrompt}
            """;

        var json = await _geminiClient.GenerateJsonAsync(systemPrompt, userMessage, ct);
        var draft = DeserializeDraft(json);

        return draft with { SourceType = SourceType.AIGenerated };
    }

    private async Task<string> LoadSystemPromptAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var path = _options.SystemPromptFilePath;
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"System prompt file not found at '{path}'. Ensure GeminiOptions.SystemPromptFilePath points to a valid file.");
        }

        return await File.ReadAllTextAsync(path, ct);
    }

    private bool IsKnownStrategyType(string strategyType)
    {
        // "composite" is always valid even if not yet registered in the assembly scan
        if (string.Equals(strategyType, "composite", StringComparison.OrdinalIgnoreCase))
            return true;

        return _registry.KnownNames.Contains(strategyType, StringComparer.OrdinalIgnoreCase);
    }

    private string BuildCorrectionPrompt(string unknownType)
    {
        var knownNames = string.Join(", ", _registry.KnownNames);
        return $"""
            The strategy type '{unknownType}' is not recognised. 
            Please choose from the following known strategy types: {knownNames}
            
            Regenerate the strategy draft using one of the known types listed above.
            """;
    }

    private static string BuildCompositeValidationCorrectionPrompt(
        AIStrategyDraft draft,
        IReadOnlyList<string> validationErrors)
    {
        var errorList = string.Join("\n", validationErrors.Select((e, i) => $"  {i + 1}. {e}"));
        return $"""
            The composite strategy configuration you returned has validation errors:
            {errorList}

            Original configuration:
            - Name: {draft.CompositeConfig?.Name ?? "(none)"}
            - Entry Condition: {draft.CompositeConfig?.EntryCondition ?? "(none)"}
            - Exit Condition: {draft.CompositeConfig?.ExitCondition ?? "(none)"}

            Please fix the validation errors and regenerate the composite strategy configuration.
            Ensure all indicator IDs referenced in conditions are defined in the indicators list,
            all indicator types are supported (sma, ema, rsi, macd, bollinger, atr, stochastic, donchian),
            and all condition expressions use valid syntax.
            """;
    }

    private static string BuildMetricsContext(BacktestResult result)
    {
        return $"""
            - Sharpe Ratio: {result.SharpeRatio?.ToString("F4") ?? "N/A"}
            - Max Drawdown: {result.MaxDrawdown:P2}
            - Win Rate: {result.WinRate?.ToString("P2") ?? "N/A"}
            - Trade Count: {result.TotalTrades}
            - Deflated Sharpe Ratio: {result.DeflatedSharpeRatio?.ToString("F4") ?? "N/A"}
            """;
    }

    private static AIStrategyDraft DeserializeDraft(string json)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var dto = JsonSerializer.Deserialize<AIStrategyDraftDto>(json, options)
            ?? throw new InvalidOperationException("Failed to deserialize AI response into AIStrategyDraft.");

        CompositeStrategyConfig? compositeConfig = null;
        if (string.Equals(dto.StrategyType, "composite", StringComparison.OrdinalIgnoreCase)
            && dto.CompositeConfig is not null)
        {
            compositeConfig = MapCompositeConfig(dto.CompositeConfig);
        }

        return new AIStrategyDraft(
            StrategyName: dto.StrategyName ?? "Unnamed Strategy",
            Hypothesis: dto.Hypothesis ?? "",
            StrategyType: dto.StrategyType ?? "",
            Parameters: dto.Parameters ?? new Dictionary<string, object>(),
            SuggestedRisk: new RiskConfig(
                dto.SuggestedRisk?.RiskParameters ?? new Dictionary<string, object>(),
                dto.SuggestedRisk?.InitialCash ?? 100_000m,
                dto.SuggestedRisk?.AnnualRiskFreeRate ?? 0.05m),
            Rationale: dto.Rationale ?? "",
            Caveats: dto.Caveats ?? new List<string>(),
            CompositeConfig: compositeConfig,
            SourceType: SourceType.AIGenerated);
    }

    private static CompositeStrategyConfig MapCompositeConfig(CompositeConfigDto dto)
    {
        var indicators = dto.Indicators?
            .Select(i => new IndicatorConfig(
                Id: i.Id ?? "",
                Type: i.Type ?? "",
                Parameters: i.Parameters?.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value) as IReadOnlyDictionary<string, object>
                    ?? new Dictionary<string, object>()))
            .ToList() as IReadOnlyList<IndicatorConfig>
            ?? new List<IndicatorConfig>();

        var directionMode = DirectionMode.Long;
        if (!string.IsNullOrWhiteSpace(dto.DirectionMode)
            && Enum.TryParse<DirectionMode>(dto.DirectionMode, ignoreCase: true, out var parsed))
        {
            directionMode = parsed;
        }

        return new CompositeStrategyConfig(
            Name: dto.Name ?? "Unnamed Composite Strategy",
            Indicators: indicators,
            EntryCondition: dto.EntryCondition ?? "",
            ExitCondition: dto.ExitCondition ?? "",
            DirectionMode: directionMode);
    }

    /// <summary>
    /// Internal DTO for JSON deserialization of AI responses.
    /// </summary>
    private sealed class AIStrategyDraftDto
    {
        public string? StrategyName { get; set; }
        public string? Hypothesis { get; set; }
        public string? StrategyType { get; set; }
        public Dictionary<string, object>? Parameters { get; set; }
        public RiskConfigDto? SuggestedRisk { get; set; }
        public string? Rationale { get; set; }
        public List<string>? Caveats { get; set; }
        public CompositeConfigDto? CompositeConfig { get; set; }
    }

    private sealed class RiskConfigDto
    {
        public Dictionary<string, object>? RiskParameters { get; set; }
        public decimal InitialCash { get; set; } = 100_000m;
        public decimal AnnualRiskFreeRate { get; set; } = 0.05m;
    }

    /// <summary>
    /// Internal DTO for deserializing the composite strategy configuration from AI responses.
    /// Maps to <see cref="CompositeStrategyConfig"/>.
    /// </summary>
    private sealed class CompositeConfigDto
    {
        public string? Name { get; set; }
        public List<IndicatorConfigDto>? Indicators { get; set; }
        public string? EntryCondition { get; set; }
        public string? ExitCondition { get; set; }
        public string? DirectionMode { get; set; }
    }

    /// <summary>
    /// Internal DTO for deserializing indicator configuration entries from AI responses.
    /// Maps to <see cref="IndicatorConfig"/>.
    /// </summary>
    private sealed class IndicatorConfigDto
    {
        public string? Id { get; set; }
        public string? Type { get; set; }
        public Dictionary<string, object>? Parameters { get; set; }
    }
}
