using System.Net.Http.Headers;
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
/// Translates strategy ideas via the Groq API's OpenAI-compatible
/// <c>/chat/completions</c> endpoint with JSON response format.
/// </summary>
public sealed class GroqTranslator : IStrategyIdeaTranslator
{
    private readonly HttpClient _httpClient;
    private readonly IPromptLoader _promptLoader;
    private readonly LlmProviderOptions _options;
    private readonly ILogger<GroqTranslator> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public GroqTranslator(
        IHttpClientFactory httpClientFactory,
        IPromptLoader promptLoader,
        IOptions<LlmProviderOptions> options,
        ILogger<GroqTranslator> logger)
    {
        _httpClient = httpClientFactory.CreateClient("Groq");
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

            var baseUrl = _options.GroqBaseUrl.TrimEnd('/');
            var requestUri = $"{baseUrl}/chat/completions";

            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
            request.Content = new StringContent(
                JsonSerializer.Serialize(requestBody, JsonOptions),
                Encoding.UTF8,
                "application/json");

            if (!string.IsNullOrEmpty(_options.GroqApiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.GroqApiKey);
            }

            using var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(ct);
            return ParseResponse(responseJson);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Groq translation failed");
            return new StrategyIdeaResult(false, FailureReason: $"Groq error: {ex.Message}");
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
            model = _options.GroqModel,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userDescription }
            },
            response_format = new { type = "json_object" },
            temperature = 0.3
        };
    }

    private static StrategyIdeaResult ParseResponse(string responseJson)
    {
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        // Navigate OpenAI-compatible response: choices[0].message.content
        var content = root
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        if (string.IsNullOrWhiteSpace(content))
            return new StrategyIdeaResult(false, FailureReason: "Empty response from Groq.");

        return GoogleAiStudioTranslator.DeserializeResult(content);
    }
}
