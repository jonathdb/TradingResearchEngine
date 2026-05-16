using System.Reflection;
using TradingResearchEngine.Application.Engine;
using TradingResearchEngine.Core.Engine;
using TradingResearchEngine.Infrastructure.DataProviders;
using TradingResearchEngine.Web.Helpers;

namespace TradingResearchEngine.IntegrationTests.Architecture;

/// <summary>
/// Enforces the solution dependency rule in CI:
///   Core ← Application ← Infrastructure ← Web
///
/// Core has no references to other solution projects.
/// Application references Core only.
/// Infrastructure references Application and Core.
/// Web references Infrastructure and Application.
///
/// Complements the IDE-only .kiro/hooks/architecture-check.md hook.
/// </summary>
public sealed class ArchitectureDependencyTests
{
    private const string CoreAssemblyName = "TradingResearchEngine.Core";
    private const string ApplicationAssemblyName = "TradingResearchEngine.Application";
    private const string InfrastructureAssemblyName = "TradingResearchEngine.Infrastructure";
    private const string WebAssemblyName = "TradingResearchEngine.Web";

    private static readonly Assembly CoreAssembly = typeof(BacktestEngine).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(RunScenarioUseCase).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(CsvDataProvider).Assembly;
    private static readonly Assembly WebAssembly = typeof(ChartDataHelpers).Assembly;

    /// <summary>
    /// Returns the names of all solution project assemblies referenced by the given assembly.
    /// Filters to only TradingResearchEngine.* assemblies to ignore framework/NuGet references.
    /// </summary>
    private static IReadOnlyList<string> GetSolutionReferences(Assembly assembly)
    {
        return assembly.GetReferencedAssemblies()
            .Where(a => a.Name is not null && a.Name.StartsWith("TradingResearchEngine.", StringComparison.Ordinal))
            .Select(a => a.Name!)
            .ToList();
    }

    [Fact]
    public void Core_DoesNotReference_Application()
    {
        var references = GetSolutionReferences(CoreAssembly);
        Assert.DoesNotContain(ApplicationAssemblyName, references);
    }

    [Fact]
    public void Core_DoesNotReference_Infrastructure()
    {
        var references = GetSolutionReferences(CoreAssembly);
        Assert.DoesNotContain(InfrastructureAssemblyName, references);
    }

    [Fact]
    public void Core_DoesNotReference_Web()
    {
        var references = GetSolutionReferences(CoreAssembly);
        Assert.DoesNotContain(WebAssemblyName, references);
    }

    [Fact]
    public void Core_HasNoSolutionReferences()
    {
        var references = GetSolutionReferences(CoreAssembly);
        Assert.Empty(references);
    }

    [Fact]
    public void Application_DoesNotReference_Infrastructure()
    {
        var references = GetSolutionReferences(ApplicationAssembly);
        Assert.DoesNotContain(InfrastructureAssemblyName, references);
    }

    [Fact]
    public void Application_DoesNotReference_Web()
    {
        var references = GetSolutionReferences(ApplicationAssembly);
        Assert.DoesNotContain(WebAssemblyName, references);
    }

    [Fact]
    public void Application_OnlyReferences_Core()
    {
        var references = GetSolutionReferences(ApplicationAssembly);
        Assert.All(references, r => Assert.Equal(CoreAssemblyName, r));
    }

    [Fact]
    public void Infrastructure_DoesNotReference_Web()
    {
        var references = GetSolutionReferences(InfrastructureAssembly);
        Assert.DoesNotContain(WebAssemblyName, references);
    }

    [Fact]
    public void Infrastructure_OnlyReferences_CoreAndApplication()
    {
        var references = GetSolutionReferences(InfrastructureAssembly);
        var allowed = new HashSet<string> { CoreAssemblyName, ApplicationAssemblyName };
        Assert.All(references, r => Assert.Contains(r, allowed));
    }

    [Fact]
    public void Web_OnlyReferences_AllowedProjects()
    {
        var references = GetSolutionReferences(WebAssembly);
        var allowed = new HashSet<string>
        {
            CoreAssemblyName,
            ApplicationAssemblyName,
            InfrastructureAssemblyName
        };
        Assert.All(references, r => Assert.Contains(r, allowed));
    }
}
