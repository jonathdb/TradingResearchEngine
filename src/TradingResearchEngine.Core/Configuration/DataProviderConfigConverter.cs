using System.Text.Json;
using System.Text.Json.Serialization;

namespace TradingResearchEngine.Core.Configuration;

/// <summary>
/// Custom JSON converter for <see cref="DataProviderConfig"/> that supports both:
/// <list type="bullet">
///   <item>New discriminated union format with <c>$type</c> discriminator</item>
///   <item>Legacy dictionary format (flat key-value pairs without discriminator)</item>
/// </list>
/// <para>
/// When reading legacy JSON without a <c>$type</c> field, the converter infers the provider
/// type from the presence of characteristic keys (e.g. "FilePath" → CSV, "BaseUrl" → HTTP,
/// "CacheDirectory" → Dukascopy). This maintains backward compatibility with existing
/// configuration files.
/// </para>
/// </summary>
public sealed class DataProviderConfigConverter : JsonConverter<DataProviderConfig>
{
    /// <inheritdoc/>
    public override DataProviderConfig? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected StartObject token for DataProviderConfig.");

        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        // Check for explicit type discriminator
        if (root.TryGetProperty("$type", out var typeElement))
        {
            var discriminator = typeElement.GetString();
            return discriminator switch
            {
                "csv" => DeserializeCsv(root),
                "http" => DeserializeHttp(root),
                "dukascopy" => DeserializeDukascopy(root),
                _ => throw new JsonException($"Unknown DataProviderConfig discriminator: '{discriminator}'")
            };
        }

        // Legacy format: infer type from characteristic keys
        return InferFromKeys(root);
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, DataProviderConfig value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("$type", value.ProviderType);

        // Write common fields
        if (value.Symbol is not null)
            writer.WriteString("Symbol", value.Symbol);
        if (value.Interval is not null)
            writer.WriteString("Interval", value.Interval);
        if (value.From.HasValue)
            writer.WriteString("From", value.From.Value);
        if (value.To.HasValue)
            writer.WriteString("To", value.To.Value);

        // Write type-specific fields
        switch (value)
        {
            case CsvDataProviderConfig csv:
                writer.WriteString("FilePath", csv.FilePath);
                writer.WriteString("DateFormat", csv.DateFormat);
                writer.WriteBoolean("HasHeader", csv.HasHeader);
                break;

            case HttpDataProviderConfig http:
                writer.WriteString("BaseUrl", http.BaseUrl);
                if (!string.IsNullOrEmpty(http.ApiKey))
                    writer.WriteString("ApiKey", http.ApiKey);
                writer.WriteNumber("TimeoutSeconds", http.TimeoutSeconds);
                break;

            case DukascopyDataProviderConfig dukascopy:
                writer.WriteString("CacheDirectory", dukascopy.CacheDirectory);
                writer.WriteNumber("CacheTtlHours", dukascopy.CacheTtlHours);
                break;
        }

        writer.WriteEndObject();
    }

    private static DataProviderConfig InferFromKeys(JsonElement root)
    {
        // Infer CSV: has FilePath or HasHeader
        if (root.TryGetProperty("FilePath", out _) || root.TryGetProperty("HasHeader", out _))
            return DeserializeCsv(root);

        // Infer HTTP: has BaseUrl
        if (root.TryGetProperty("BaseUrl", out _))
            return DeserializeHttp(root);

        // Infer Dukascopy: has CacheDirectory or CacheTtl/CacheTtlHours
        if (root.TryGetProperty("CacheDirectory", out _) || root.TryGetProperty("CacheTtlHours", out _)
            || root.TryGetProperty("CacheTtl", out _))
            return DeserializeDukascopy(root);

        // Default to CSV with whatever common fields are present
        return DeserializeCsv(root);
    }

    private static CsvDataProviderConfig DeserializeCsv(JsonElement root)
    {
        return new CsvDataProviderConfig
        {
            Symbol = GetStringOrNull(root, "Symbol"),
            Interval = GetStringOrNull(root, "Interval"),
            From = GetDateTimeOffsetOrNull(root, "From"),
            To = GetDateTimeOffsetOrNull(root, "To"),
            FilePath = GetStringOrDefault(root, "FilePath", ""),
            DateFormat = GetStringOrDefault(root, "DateFormat", "yyyy-MM-dd"),
            HasHeader = GetBoolOrDefault(root, "HasHeader", true)
        };
    }

    private static HttpDataProviderConfig DeserializeHttp(JsonElement root)
    {
        var timeoutSeconds = 30;
        if (root.TryGetProperty("TimeoutSeconds", out var ts) && ts.TryGetInt32(out var tsVal))
            timeoutSeconds = tsVal;
        else if (root.TryGetProperty("Timeout", out var t) && t.TryGetInt32(out var tVal))
            timeoutSeconds = tVal;

        return new HttpDataProviderConfig
        {
            Symbol = GetStringOrNull(root, "Symbol"),
            Interval = GetStringOrNull(root, "Interval"),
            From = GetDateTimeOffsetOrNull(root, "From"),
            To = GetDateTimeOffsetOrNull(root, "To"),
            BaseUrl = GetStringOrDefault(root, "BaseUrl", ""),
            ApiKey = GetStringOrDefault(root, "ApiKey", ""),
            TimeoutSeconds = timeoutSeconds
        };
    }

    private static DukascopyDataProviderConfig DeserializeDukascopy(JsonElement root)
    {
        var cacheTtlHours = 24;
        if (root.TryGetProperty("CacheTtlHours", out var cth) && cth.TryGetInt32(out var cthVal))
            cacheTtlHours = cthVal;
        else if (root.TryGetProperty("CacheTtl", out var ct) && ct.TryGetInt32(out var ctVal))
            cacheTtlHours = ctVal;

        return new DukascopyDataProviderConfig
        {
            Symbol = GetStringOrNull(root, "Symbol"),
            Interval = GetStringOrNull(root, "Interval"),
            From = GetDateTimeOffsetOrNull(root, "From"),
            To = GetDateTimeOffsetOrNull(root, "To"),
            CacheDirectory = GetStringOrDefault(root, "CacheDirectory", "data/dukascopy-cache"),
            CacheTtlHours = cacheTtlHours
        };
    }

    private static string? GetStringOrNull(JsonElement root, string propertyName)
    {
        if (root.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
        return null;
    }

    private static string GetStringOrDefault(JsonElement root, string propertyName, string defaultValue)
    {
        if (root.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString() ?? defaultValue;
        return defaultValue;
    }

    private static bool GetBoolOrDefault(JsonElement root, string propertyName, bool defaultValue)
    {
        if (root.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.True) return true;
            if (prop.ValueKind == JsonValueKind.False) return false;
        }
        return defaultValue;
    }

    private static DateTimeOffset? GetDateTimeOffsetOrNull(JsonElement root, string propertyName)
    {
        if (root.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            var str = prop.GetString();
            if (str is not null && DateTimeOffset.TryParse(str, out var parsed))
                return parsed;
        }
        return null;
    }
}
