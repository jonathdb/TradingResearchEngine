using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingResearchEngine.Application.AI;
using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Application.Strategy;

namespace TradingResearchEngine.Infrastructure.AI;

/// <summary>
/// Translates strategy ideas via a local Ollama instance using the
/// <c>/api/generate</c> endpoint with <c>format: "json"</c>.
/// </summary>
public sealed class OllamaTranslator : IStrategyIdeaTranslator
{
    private readonly HttpClient _httpClient;
    private readonly IPromptLoader _promptLoader;
    private readonly LlmProviderOptions _options;
    private readonly ILogger<OllamaTranslator> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public OllamaTranslator(
        IHttpClientFactory httpClientFactory,
        IPromptLoader promptLoader,
        IOptions<LlmProviderOptions> options,
        ILogger<OllamaTranslator> logger)
    {
        _httpClient = httpClientFactory.CreateClient("Ollama");
        _promptLoader = promptLoader;
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
        try
        {
            var systemPrompt = BuildSystemPrompt(templates, schemas);
            var requestBody = BuildRequestBody(systemPrompt, userDescription);

            var baseUrl = _options.OllamaBaseUrl.TrimEnd('/');
            var requestUri = $"{baseUrl}/api/generate";

            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
            request.Content = new StringContent(
                JsonSerializer.Serialize(requestBody, JsonOptions),
                Encoding.UTF8,
                "application/json");

            using var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(ct);
            return ParseResponse(responseJson);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Ollama translation failed");
            return new StrategyIdeaResult(false, FailureReason: $"Ollama error: {ex.Message}");
        }
    }

    private string BuildSystemPrompt(
        IReadOnlyList<StrategyTemplate> templates,
        IReadOnlyList<StrategyParameterSchema> schemas)
    {
        var templatesJson = JsonSerializer.Serialize(
            templates.Select(t => new
            {
                t.TemplateId,
                t.Name,
                t.Description,
                t.StrategyType,
                t.TypicalUseCase,
                t.DefaultParameters,
                t.RecommendedTimeframe
            }),
            JsonOptions);

        var schemasJson = JsonSerializer.Serialize(
            schemas.Select(s => new
            {
                s.Name,
                s.DisplayName,
                s.Type,
                s.DefaultValue,
                s.Min,
                s.Max,
                s.Description
            }),
            JsonOptions);

        return _promptLoader.GetPrompt(
            "strategy-idea-translator-system-prompt",
            new Dictionary<string, string>
            {
                ["templates_json"] = templatesJson,
                ["schemas_json"] = schemasJson
            });
    }

    private object BuildRequestBody(string systemPrompt, string userDescription)
    {
        return new
        {
            model = _options.OllamaModel,
            system = systemPrompt,
            prompt = userDescription,
            format = "json",
            stream = false
        };
    }

    private static StrategyIdeaResult ParseResponse(string responseJson)
    {
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        // Ollama /api/generate response has a "response" field with the generated text
        var content = root.TryGetProperty("response", out var responseProp)
            ? responseProp.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(content))
            return new StrategyIdeaResult(false, FailureReason: "Empty response from Ollama.");

        return GoogleAiStudioTranslator.DeserializeResult(content);
    }
}
