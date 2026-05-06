using System.Text.Json;
using Skender.Stock.Indicators;
using TradingResearchEngine.Application.Indicators;

namespace TradingResearchEngine.Application.Strategy.Composite;

/// <summary>
/// Instantiates <see cref="IIndicatorInstance"/> wrappers from declarative
/// <see cref="IndicatorConfig"/> records. Supports all 8 existing indicator types.
/// </summary>
public static class IndicatorFactory
{
    /// <summary>
    /// The set of supported indicator type names.
    /// </summary>
    private static readonly HashSet<string> SupportedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "sma", "ema", "rsi", "macd", "bollinger", "atr", "stochastic", "donchian"
    };

    /// <summary>
    /// Creates an <see cref="IIndicatorInstance"/> from the specified indicator configuration.
    /// </summary>
    /// <param name="config">The indicator configuration specifying type, parameters, and ID.</param>
    /// <returns>A fully configured <see cref="IIndicatorInstance"/> ready to receive bars.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the indicator type is unknown or a required parameter is missing.
    /// </exception>
    public static IIndicatorInstance Create(IndicatorConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var type = config.Type?.Trim() ?? string.Empty;

        if (!SupportedTypes.Contains(type))
        {
            throw new ArgumentException(
                $"Unknown indicator type '{type}'. Supported types: {string.Join(", ", SupportedTypes.Order())}.",
                nameof(config));
        }

        return type.ToLowerInvariant() switch
        {
            "sma" => CreateSma(config),
            "ema" => CreateEma(config),
            "rsi" => CreateRsi(config),
            "macd" => CreateMacd(config),
            "bollinger" => CreateBollinger(config),
            "atr" => CreateAtr(config),
            "stochastic" => CreateStochastic(config),
            "donchian" => CreateDonchian(config),
            _ => throw new ArgumentException(
                $"Unknown indicator type '{type}'. Supported types: {string.Join(", ", SupportedTypes.Order())}.",
                nameof(config))
        };
    }

    private static IIndicatorInstance CreateSma(IndicatorConfig config)
    {
        var period = GetRequiredInt(config, "period");
        var series = new SmaIndicator(period);
        return new SingleValueIndicatorAdapter<SmaResult>(
            config.Id, config.Type, series, r => r.Sma);
    }

    private static IIndicatorInstance CreateEma(IndicatorConfig config)
    {
        var period = GetRequiredInt(config, "period");
        var series = new EmaIndicator(period);
        return new SingleValueIndicatorAdapter<EmaResult>(
            config.Id, config.Type, series, r => r.Ema);
    }

    private static IIndicatorInstance CreateRsi(IndicatorConfig config)
    {
        var period = GetRequiredInt(config, "period");
        var series = new RsiIndicator(period);
        return new SingleValueIndicatorAdapter<RsiResult>(
            config.Id, config.Type, series, r => r.Rsi);
    }

    private static IIndicatorInstance CreateMacd(IndicatorConfig config)
    {
        var fastPeriod = GetOptionalInt(config, "fastPeriod", 12);
        var slowPeriod = GetOptionalInt(config, "slowPeriod", 26);
        var signalPeriod = GetOptionalInt(config, "signalPeriod", 9);
        var series = new MacdIndicator(fastPeriod, slowPeriod, signalPeriod);

        var subExtractors = new Dictionary<string, Func<MacdResult, double?>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Macd"] = r => r.Macd,
            ["Signal"] = r => r.Signal,
            ["Histogram"] = r => r.Histogram
        };

        return new MultiValueIndicatorAdapter<MacdResult>(
            config.Id, config.Type, series, r => r.Macd, subExtractors);
    }

    private static IIndicatorInstance CreateBollinger(IndicatorConfig config)
    {
        var period = GetOptionalInt(config, "period", 20);
        var standardDeviations = GetOptionalDouble(config, "standardDeviations", 2.0);
        var series = new BollingerBandsIndicator(period, standardDeviations);

        var subExtractors = new Dictionary<string, Func<BollingerBandsResult, double?>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Upper"] = r => r.UpperBand,
            ["Middle"] = r => r.Sma,
            ["Lower"] = r => r.LowerBand
        };

        return new MultiValueIndicatorAdapter<BollingerBandsResult>(
            config.Id, config.Type, series, r => r.Sma, subExtractors);
    }

    private static IIndicatorInstance CreateAtr(IndicatorConfig config)
    {
        var period = GetRequiredInt(config, "period");
        var series = new AtrIndicator(period);
        return new SingleValueIndicatorAdapter<AtrResult>(
            config.Id, config.Type, series, r => r.Atr);
    }

    private static IIndicatorInstance CreateStochastic(IndicatorConfig config)
    {
        var lookbackPeriod = GetOptionalInt(config, "lookbackPeriod", 14);
        var signalPeriod = GetOptionalInt(config, "signalPeriod", 3);
        var smoothPeriod = GetOptionalInt(config, "smoothPeriod", 3);
        var series = new StochasticIndicator(lookbackPeriod, signalPeriod, smoothPeriod);

        var subExtractors = new Dictionary<string, Func<StochResult, double?>>(StringComparer.OrdinalIgnoreCase)
        {
            ["K"] = r => r.K,
            ["D"] = r => r.D
        };

        return new MultiValueIndicatorAdapter<StochResult>(
            config.Id, config.Type, series, r => r.K, subExtractors);
    }

    private static IIndicatorInstance CreateDonchian(IndicatorConfig config)
    {
        var period = GetRequiredInt(config, "period");
        var series = new DonchianIndicator(period);

        var subExtractors = new Dictionary<string, Func<DonchianResult, double?>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Upper"] = r => (double?)r.UpperBand,
            ["Lower"] = r => (double?)r.LowerBand,
            ["Middle"] = r => (double?)r.Centerline
        };

        return new MultiValueIndicatorAdapter<DonchianResult>(
            config.Id, config.Type, series, r => (double?)r.UpperBand, subExtractors);
    }

    /// <summary>
    /// Gets a required integer parameter from the config, throwing if missing.
    /// Handles both raw int values and <see cref="JsonElement"/> from JSON deserialization.
    /// </summary>
    private static int GetRequiredInt(IndicatorConfig config, string paramName)
    {
        if (config.Parameters is null || !TryGetParameter(config.Parameters, paramName, out var value))
        {
            throw new ArgumentException(
                $"Missing required parameter '{paramName}' for indicator '{config.Id}' (type: {config.Type}).",
                nameof(config));
        }

        return ConvertToInt(value, paramName, config);
    }

    /// <summary>
    /// Gets an optional integer parameter from the config, returning the default if missing.
    /// Handles both raw int values and <see cref="JsonElement"/> from JSON deserialization.
    /// </summary>
    private static int GetOptionalInt(IndicatorConfig config, string paramName, int defaultValue)
    {
        if (config.Parameters is null || !TryGetParameter(config.Parameters, paramName, out var value))
            return defaultValue;

        return ConvertToInt(value, paramName, config);
    }

    /// <summary>
    /// Gets an optional double parameter from the config, returning the default if missing.
    /// Handles both raw double values and <see cref="JsonElement"/> from JSON deserialization.
    /// </summary>
    private static double GetOptionalDouble(IndicatorConfig config, string paramName, double defaultValue)
    {
        if (config.Parameters is null || !TryGetParameter(config.Parameters, paramName, out var value))
            return defaultValue;

        return ConvertToDouble(value, paramName, config);
    }

    /// <summary>
    /// Attempts to get a parameter value from the dictionary using case-insensitive key matching.
    /// </summary>
    private static bool TryGetParameter(IReadOnlyDictionary<string, object> parameters, string paramName, out object value)
    {
        // Try exact match first
        if (parameters.TryGetValue(paramName, out value!))
            return true;

        // Fall back to case-insensitive search
        foreach (var kvp in parameters)
        {
            if (string.Equals(kvp.Key, paramName, StringComparison.OrdinalIgnoreCase))
            {
                value = kvp.Value;
                return true;
            }
        }

        value = null!;
        return false;
    }

    /// <summary>
    /// Converts a parameter value (which may be a raw type or JsonElement) to int.
    /// </summary>
    private static int ConvertToInt(object value, string paramName, IndicatorConfig config)
    {
        return value switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            float f => (int)f,
            JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetInt32(),
            _ => throw new ArgumentException(
                $"Parameter '{paramName}' for indicator '{config.Id}' (type: {config.Type}) must be an integer, but got '{value}'.",
                nameof(config))
        };
    }

    /// <summary>
    /// Converts a parameter value (which may be a raw type or JsonElement) to double.
    /// </summary>
    private static double ConvertToDouble(object value, string paramName, IndicatorConfig config)
    {
        return value switch
        {
            double d => d,
            float f => f,
            int i => i,
            long l => l,
            decimal dec => (double)dec,
            JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetDouble(),
            _ => throw new ArgumentException(
                $"Parameter '{paramName}' for indicator '{config.Id}' (type: {config.Type}) must be a number, but got '{value}'.",
                nameof(config))
        };
    }
}
