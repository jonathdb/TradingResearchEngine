using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Strategy;

namespace TradingResearchEngine.Application.Strategies;

/// <summary>
/// Generic <see cref="IStrategyFactory"/> implementation that creates strategy instances
/// via constructor reflection, matching parameters from <see cref="StrategyConfig.Parameters"/>.
/// Thread-safe: may be called concurrently from multiple threads.
/// </summary>
public sealed class ReflectionStrategyFactory : IStrategyFactory
{
    private readonly Type _strategyType;
    private readonly IServiceProvider _services;
    private readonly System.Reflection.ConstructorInfo[] _ctors;

    /// <inheritdoc/>
    public string StrategyType { get; }

    /// <summary>Creates a factory for the given strategy type and name.</summary>
    public ReflectionStrategyFactory(string strategyType, Type strategyClrType, IServiceProvider services)
    {
        StrategyType = strategyType;
        _strategyType = strategyClrType;
        _services = services;
        _ctors = strategyClrType.GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .ToArray();
    }

    /// <inheritdoc/>
    public IStrategy Create(StrategyConfig config)
    {
        var parameters = config.StrategyParameters;

        foreach (var ctor in _ctors)
        {
            var ctorParams = ctor.GetParameters();
            var args = new object?[ctorParams.Length];
            bool allResolved = true;

            for (int i = 0; i < ctorParams.Length; i++)
            {
                var p = ctorParams[i];
                var match = parameters.FirstOrDefault(kv =>
                    string.Equals(kv.Key, p.Name, StringComparison.OrdinalIgnoreCase));

                if (match.Key is not null)
                {
                    try
                    {
                        var rawValue = match.Value;
                        if (rawValue is JsonElement je)
                            rawValue = ConvertJsonElement(je, p.ParameterType);
                        args[i] = Convert.ChangeType(rawValue, p.ParameterType);
                        continue;
                    }
                    catch { /* fall through to default */ }
                }

                if (p.HasDefaultValue) { args[i] = p.DefaultValue; continue; }

                allResolved = false;
                break;
            }

            if (allResolved)
                return (IStrategy)ctor.Invoke(args);
        }

        return (IStrategy)ActivatorUtilities.CreateInstance(_services, _strategyType);
    }

    private static object? ConvertJsonElement(JsonElement element, Type targetType)
    {
        if (targetType == typeof(int)) return element.GetInt32();
        if (targetType == typeof(decimal)) return element.GetDecimal();
        if (targetType == typeof(double)) return element.GetDouble();
        if (targetType == typeof(bool)) return element.GetBoolean();
        if (targetType == typeof(string)) return element.GetString();
        if (targetType == typeof(long)) return element.GetInt64();
        return element.ToString();
    }
}
