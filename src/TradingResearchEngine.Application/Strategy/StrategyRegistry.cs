using System.Reflection;
using TradingResearchEngine.Core.Strategy;

namespace TradingResearchEngine.Application.Strategy;

/// <summary>
/// Singleton registry that maps kebab-case strategy names to their <see cref="Type"/>.
/// Populated at startup via <see cref="RegisterAssembly"/>.
/// </summary>
public sealed class StrategyRegistry
{
    private readonly Dictionary<string, Type> _registry = new(StringComparer.OrdinalIgnoreCase);

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
    public Type Resolve(string strategyName)
    {
        if (_registry.TryGetValue(strategyName, out var type)) return type;
        throw new StrategyNotFoundException(strategyName, KnownNames);
    }

    /// <summary>
    /// Returns parameter metadata for the given strategy name.
    /// Inspects the constructor with the most parameters.
    /// </summary>
    public IReadOnlyList<StrategyParameterInfo> GetParameterInfo(string strategyName)
    {
        var type = Resolve(strategyName);
        var ctor = type.GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault();

        if (ctor is null) return Array.Empty<StrategyParameterInfo>();

        return ctor.GetParameters()
            .Select(p => new StrategyParameterInfo(
                p.Name ?? "",
                p.ParameterType.Name,
                p.HasDefaultValue ? p.DefaultValue : null))
            .ToList();
    }
}

/// <summary>Describes a single constructor parameter of a strategy.</summary>
public sealed record StrategyParameterInfo(
    string Name,
    string TypeName,
    object? DefaultValue);
