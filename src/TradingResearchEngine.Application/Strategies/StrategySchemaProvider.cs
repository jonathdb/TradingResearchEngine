using System.Reflection;
using System.Text.RegularExpressions;

namespace TradingResearchEngine.Application.Strategies;

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
        var defaultValue = ResolveDefault(param, meta);
        return new StrategyParameterSchema(
            Name: param.Name ?? "",
            DisplayName: meta?.DisplayName ?? FormatName(param.Name ?? ""),
            Type: MapType(param.ParameterType),
            DefaultValue: defaultValue,
            IsRequired: !param.HasDefaultValue && (meta is null || !meta.HasDefault),
            Min: meta?.Min,
            Max: meta?.Max,
            EnumChoices: param.ParameterType.IsEnum ? Enum.GetNames(param.ParameterType) : null,
            Description: meta?.Description ?? "",
            SensitivityHint: meta?.SensitivityHint ?? SensitivityHint.Medium,
            Group: meta?.Group ?? "Signal",
            IsAdvanced: meta?.IsAdvanced ?? false,
            DisplayOrder: meta?.DisplayOrder ?? index);
    }

    /// <summary>
    /// Resolves the default value for a parameter using the following precedence:
    /// 1. C# constructor default value (highest priority — language-level contract)
    /// 2. <see cref="ParameterMetaAttribute.Default"/> (schema-driven, attribute-based)
    /// 3. Type-based fallback (last resort for backward compatibility)
    /// </summary>
    private static object ResolveDefault(ParameterInfo param, ParameterMetaAttribute? meta)
    {
        // 1. Constructor default takes highest priority
        if (param.HasDefaultValue && param.DefaultValue is not null)
            return param.DefaultValue;

        // 2. Attribute-based schema default
        if (meta is { HasDefault: true, Default: not null })
        {
            try
            {
                return Convert.ChangeType(meta.Default, param.ParameterType);
            }
            catch
            {
                return meta.Default;
            }
        }

        // 3. Last-resort type-based fallback
        return GetTypeDefault(param.ParameterType);
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

    /// <summary>
    /// Last-resort fallback: returns a sensible zero/empty value for the given type.
    /// Prefer constructor defaults or <see cref="ParameterMetaAttribute.Default"/> over this method.
    /// </summary>
    private static object GetTypeDefault(Type type)
    {
        if (type == typeof(int)) return 0;
        if (type == typeof(decimal)) return 0m;
        if (type == typeof(double)) return 0.0;
        if (type == typeof(bool)) return false;
        return type.IsValueType ? Activator.CreateInstance(type)! : "";
    }
}
