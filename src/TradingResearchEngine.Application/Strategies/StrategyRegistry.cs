using System.Reflection;
using Microsoft.Extensions.Logging;
using TradingResearchEngine.Core.Strategy;

namespace TradingResearchEngine.Application.Strategies;

/// <summary>
/// Singleton registry that maps kebab-case strategy names to their <see cref="Type"/>.
/// Populated at startup via <see cref="RegisterAssembly"/>.
/// Serves as the single entry point for runtime strategy construction.
/// </summary>
public sealed class StrategyRegistry
{
    private readonly Dictionary<string, Type> _registry = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<StrategyParameterInfo>> _paramInfoCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>All strategy names currently registered.</summary>
    public IReadOnlyList<string> KnownNames => _registry.Keys.ToList();

    /// <summary>
    /// Scans <paramref name="assembly"/> for all non-abstract <see cref="IStrategy"/> implementations
    /// decorated with <see cref="StrategyNameAttribute"/> and adds them to the registry.
    /// Throws <see cref="InvalidOperationException"/> on duplicate names.
    /// </summary>
    public void RegisterAssembly(Assembly assembly)
    {
        var candidates = assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(IStrategy).IsAssignableFrom(t))
            .Select(t => (Type: t, Attr: t.GetCustomAttribute<StrategyNameAttribute>()))
            .Where(x => x.Attr is not null);

        foreach (var (type, attr) in candidates)
        {
            string name = attr!.Name;
            if (_registry.TryGetValue(name, out var existing))
                throw new InvalidOperationException(
                    $"Duplicate strategy name '{name}' found on '{type.FullName}' and '{existing.FullName}'.");
            _registry[name] = type;
        }
    }

    /// <summary>
    /// Returns the <see cref="Type"/> for the given strategy name.
    /// Throws <see cref="StrategyNotFoundException"/> when not found.
    /// </summary>
    public Type Resolve(StrategyTypeId strategyType) => Resolve(strategyType.Value);

    /// <summary>
    /// Returns the <see cref="Type"/> for the given strategy name string.
    /// Throws <see cref="StrategyNotFoundException"/> when not found.
    /// </summary>
    public Type Resolve(string strategyName)
    {
        if (_registry.TryGetValue(strategyName, out var type)) return type;
        throw new StrategyNotFoundException(strategyName, KnownNames);
    }

    /// <summary>
    /// Attempts to instantiate every registered strategy with default parameters.
    /// Returns a <see cref="StrategyVerificationResult"/> summarizing successes and failures.
    /// Used at startup to verify all registered strategies can be constructed.
    /// </summary>
    public StrategyVerificationResult VerifyAll(ILogger? logger = null)
    {
        var failures = new List<StrategyVerificationFailure>();

        foreach (var (name, type) in _registry)
        {
            try
            {
                var ctor = type.GetConstructors()
                    .OrderByDescending(c => c.GetParameters().Length)
                    .FirstOrDefault();

                if (ctor is null)
                {
                    failures.Add(new StrategyVerificationFailure(name, type.FullName ?? type.Name,
                        "No public constructor found."));
                    logger?.LogWarning(
                        "Strategy verification failed for '{StrategyName}' ({Type}): No public constructor found.",
                        name, type.FullName);
                    continue;
                }

                var ctorParams = ctor.GetParameters();
                var args = new object?[ctorParams.Length];

                for (int i = 0; i < ctorParams.Length; i++)
                {
                    var p = ctorParams[i];
                    if (p.HasDefaultValue)
                    {
                        args[i] = p.DefaultValue;
                    }
                    else
                    {
                        args[i] = GetSchemaDefault(p) ?? GetDefaultForType(p.ParameterType);
                    }
                }

                var instance = ctor.Invoke(args);
                if (instance is not IStrategy)
                {
                    failures.Add(new StrategyVerificationFailure(name, type.FullName ?? type.Name,
                        "Constructed instance does not implement IStrategy."));
                    logger?.LogWarning(
                        "Strategy verification failed for '{StrategyName}' ({Type}): Instance does not implement IStrategy.",
                        name, type.FullName);
                }
                else
                {
                    logger?.LogDebug("Strategy '{StrategyName}' ({Type}) verified successfully.", name, type.FullName);
                }
            }
            catch (Exception ex)
            {
                var message = ex.InnerException?.Message ?? ex.Message;
                failures.Add(new StrategyVerificationFailure(name, type.FullName ?? type.Name, message));
                logger?.LogWarning(ex,
                    "Strategy verification failed for '{StrategyName}' ({Type}): {Error}",
                    name, type.FullName, message);
            }
        }

        return new StrategyVerificationResult(_registry.Count, failures);
    }

    /// <summary>
    /// Returns parameter metadata for the given strategy name.
    /// Inspects the constructor with the most parameters. Results are cached.
    /// </summary>
    public IReadOnlyList<StrategyParameterInfo> GetParameterInfo(string strategyName)
    {
        if (_paramInfoCache.TryGetValue(strategyName, out var cached))
            return cached;

        var type = Resolve(strategyName);
        var ctor = type.GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault();

        if (ctor is null)
        {
            _paramInfoCache[strategyName] = Array.Empty<StrategyParameterInfo>();
            return Array.Empty<StrategyParameterInfo>();
        }

        var result = ctor.GetParameters()
            .Select(p => new StrategyParameterInfo(
                p.Name ?? "",
                p.ParameterType.Name,
                p.HasDefaultValue ? p.DefaultValue : GetSchemaDefault(p)))
            .ToList();

        _paramInfoCache[strategyName] = result;
        return result;
    }

    /// <summary>
    /// Retrieves the schema-driven default from <see cref="ParameterMetaAttribute.Default"/>
    /// when explicitly set. Returns null when no attribute-based default is declared.
    /// </summary>
    private static object? GetSchemaDefault(ParameterInfo parameter)
    {
        var meta = parameter.GetCustomAttribute<ParameterMetaAttribute>();
        if (meta is null || !meta.HasDefault) return null;

        // Convert the attribute default to the parameter's declared type when possible
        if (meta.Default is null) return null;

        try
        {
            return Convert.ChangeType(meta.Default, parameter.ParameterType);
        }
        catch
        {
            // If conversion fails, return the raw attribute value
            return meta.Default;
        }
    }

    /// <summary>
    /// Last-resort fallback: returns a sensible zero/empty value for the given type.
    /// Prefer <see cref="GetSchemaDefault"/> (attribute-driven) or constructor defaults
    /// over this method. Retained for backward compatibility with parameters that lack
    /// both a C# default value and a <see cref="ParameterMetaAttribute.Default"/> declaration.
    /// </summary>
    private static object? GetDefaultForType(Type type)
    {
        if (type == typeof(int)) return 0;
        if (type == typeof(decimal)) return 0m;
        if (type == typeof(double)) return 0.0;
        if (type == typeof(bool)) return false;
        if (type == typeof(string)) return "";
        if (type == typeof(long)) return 0L;
        if (type.IsValueType) return Activator.CreateInstance(type);
        return null;
    }
}

/// <summary>Describes a single constructor parameter of a strategy.</summary>
public sealed record StrategyParameterInfo(
    string Name,
    string TypeName,
    object? DefaultValue);

/// <summary>Result of <see cref="StrategyRegistry.VerifyAll"/> startup verification.</summary>
public sealed record StrategyVerificationResult(
    int TotalRegistered,
    IReadOnlyList<StrategyVerificationFailure> Failures)
{
    /// <summary>Whether all registered strategies were verified successfully.</summary>
    public bool AllSucceeded => Failures.Count == 0;

    /// <summary>Number of strategies that failed verification.</summary>
    public int FailureCount => Failures.Count;
}

/// <summary>Describes a single strategy that failed startup verification.</summary>
public sealed record StrategyVerificationFailure(
    string StrategyName,
    string TypeName,
    string Reason);
