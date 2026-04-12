using System.Reflection;
using System.Text.RegularExpressions;

namespace TradingResearchEngine.Application.Strategy;

/// <summary>
/// Builds <see cref="StrategyParameterSchema"/> from constructor inspection
/// and optional <see cref="ParameterMetaAttribute"/> annotations.
/// </summary>
public sealed class StrategySchemaProvider : IStrategySchemaProvider
{
    private readonly StrategyRegistry _registry;

    /// <inheritdoc cref="StrategySchemaProvider"/>
    public StrategySchemaProvider(StrategyRegistry registry) => _registry = registry;

    /// <inheritdoc/>
    public IReadOnlyList<StrategyParameterSchema> GetSchema(string strategyName)
    {
        var type = _registry.Resolve(strategyName);
        var ctor = type.GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault();

        if (ctor is null) return Array.Empty<StrategyParameterSchema>();

        return ctor.GetParameters()
            .Select((p, i) => BuildSchema(p, i))
            .ToList();
    }

    private static StrategyParameterSchema BuildSchema(ParameterInfo param, int index)
    {
        var meta = param.GetCustomAttribute<ParameterMetaAttribute>();
        return new StrategyParameterSchema(
            Name: param.Name ?? "",
            DisplayName: meta?.DisplayName ?? FormatName(param.Name ?? ""),
            Type: MapType(param.ParameterType),
            DefaultValue: param.HasDefaultValue ? param.DefaultValue! : GetTypeDefault(param.ParameterType),
            IsRequired: !param.HasDefaultValue,
            Min: meta?.Min,
            Max: meta?.Max,
            EnumChoices: param.ParameterType.IsEnum ? Enum.GetNames(param.ParameterType) : null,
            Description: meta?.Description ?? "",
            SensitivityHint: meta?.SensitivityHint ?? SensitivityHint.Medium,
            Group: meta?.Group ?? "Signal",
            IsAdvanced: meta?.IsAdvanced ?? false,
            DisplayOrder: meta?.DisplayOrder ?? index);
    }

    private static string FormatName(string camelCase) =>
        Regex.Replace(camelCase, "([a-z])([A-Z])", "$1 $2");

    private static string MapType(Type type)
    {
        if (type == typeof(int)) return "int";
        if (type == typeof(decimal)) return "decimal";
        if (type == typeof(double)) return "decimal";
        if (type == typeof(bool)) return "bool";
        if (type.IsEnum) return "enum";
        return type.Name.ToLowerInvariant();
    }

    private static object GetTypeDefault(Type type)
    {
        if (type == typeof(int)) return 0;
        if (type == typeof(decimal)) return 0m;
        if (type == typeof(double)) return 0.0;
        if (type == typeof(bool)) return false;
        return type.IsValueType ? Activator.CreateInstance(type)! : "";
    }
}
