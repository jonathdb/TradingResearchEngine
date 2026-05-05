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
/// Translates strategy ideas via Google AI Studio's OpenAI-compatible
/// <c>/chat/completions</c> endpoint with JSON schema enforcement.
/// </summary>
public sealed class GoogleAiStudioTranslator : IStrategyIdeaTranslator
{
    private readonly HttpClient _httpClient;
    private readonly IPromptLoader _promptLoader;
    private readonly LlmProviderOptions _options;
    private readonly ILogger<GoogleAiStudioTranslator> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public GoogleAiStudioTranslator(
        IHttpClientFactory httpClientFactory,
        IPromptLoader promptLoader,
        IOptions<LlmProviderOptions> options,
        ILogger<GoogleAiStudioTranslator> logger)
    {
        _httpClient = httpClientFactory.CreateClient("GoogleAiStudio");
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

            var baseUrl = _options.BaseUrl.TrimEnd('/');
            var requestUri = $"{baseUrl}/chat/completions";

            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
            request.Content = new StringContent(
                JsonSerializer.Serialize(requestBody, JsonOptions),
                Encoding.UTF8,
                "application/json");

            if (!string.IsNullOrEmpty(_options.ApiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
            }

            using var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(ct);
            return ParseResponse(responseJson);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "GoogleAiStudio translation failed");
            return new StrategyIdeaResult(false, FailureReason: $"GoogleAiStudio error: {ex.Message}");
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
            model = _options.Model,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userDescription }
            },
            response_format = new
            {
                type = "json_schema",
                json_schema = new
                {
                    name = "strategy_idea_result",
                    strict = true,
                    schema = GetResponseSchema()
                }
            },
            temperature = 0.3
        };
    }

    private static object GetResponseSchema()
    {
        return new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["success"] = new { type = "boolean" },
                ["selectedTemplateId"] = new { type = new[] { "string", "null" } },
                ["strategyType"] = new { type = new[] { "string", "null" } },
                ["suggestedParameters"] = new { type = new[] { "object", "null" } },
                ["suggestedHypothesis"] = new { type = new[] { "string", "null" } },
                ["suggestedTimeframe"] = new { type = new[] { "string", "null" } },
                ["failureReason"] = new { type = new[] { "string", "null" } },
                ["generatedStrategyCode"] = new { type = new[] { "string", "null" } }
            },
            required = new[] { "success" },
            additionalProperties = false
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
            return new StrategyIdeaResult(false, FailureReason: "Empty response from GoogleAiStudio.");

        return DeserializeResult(content);
    }

    internal static StrategyIdeaResult DeserializeResult(string content)
    {
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        var success = root.TryGetProperty("success", out var successProp) && successProp.GetBoolean();

        string? selectedTemplateId = root.TryGetProperty("selectedTemplateId", out var tid)
            ? tid.GetString() : null;
        string? strategyType = root.TryGetProperty("strategyType", out var st)
            ? st.GetString() : null;
        string? suggestedHypothesis = root.TryGetProperty("suggestedHypothesis", out var sh)
            ? sh.GetString() : null;
        string? suggestedTimeframe = root.TryGetProperty("suggestedTimeframe", out var stf)
            ? stf.GetString() : null;
        string? failureReason = root.TryGetProperty("failureReason", out var fr)
            ? fr.GetString() : null;
        string? generatedStrategyCode = root.TryGetProperty("generatedStrategyCode", out var gsc)
            ? gsc.GetString() : null;

        Dictionary<string, object>? suggestedParameters = null;
        if (root.TryGetProperty("suggestedParameters", out var sp) &&
            sp.ValueKind == JsonValueKind.Object)
        {
            suggestedParameters = new Dictionary<string, object>();
            foreach (var prop in sp.EnumerateObject())
            {
                suggestedParameters[prop.Name] = ParseJsonValue(prop.Value);
            }
        }

        return new StrategyIdeaResult(
            success,
            selectedTemplateId,
            strategyType,
            suggestedParameters,
            suggestedHypothesis,
            suggestedTimeframe,
            failureReason,
            generatedStrategyCode);
    }

    private static object ParseJsonValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetInt32(out var i) => i,
            JsonValueKind.Number when element.TryGetInt64(out var l) => l,
            JsonValueKind.Number => element.GetDecimal(),
            JsonValueKind.String => element.GetString()!,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => element.GetRawText()
        };
    }
}
