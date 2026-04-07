using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TradingResearchEngine.Application;
using TradingResearchEngine.Application.Engine;
using TradingResearchEngine.Cli.Interactive;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Reporting;
using TradingResearchEngine.Infrastructure;

var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var services = new ServiceCollection();
services.AddTradingResearchEngine(configuration);
services.AddTradingResearchEngineInfrastructure(configuration);
services.AddStrategyAssembly(typeof(TradingResearchEngine.Application.Strategies.SmaCrossoverStrategy).Assembly);
services.AddLogging();

var sp = services.BuildServiceProvider();
var useCase = sp.GetRequiredService<RunScenarioUseCase>();
var reporter = sp.GetRequiredService<IReporter>();

// Parse arguments: --scenario <path> --output <path> --simulations <int>
string? scenarioPath = null;
string? outputPath = null;

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--scenario" && i + 1 < args.Length) scenarioPath = args[++i];
    else if (args[i] == "--output" && i + 1 < args.Length) outputPath = args[++i];
    else if (args[i] == "--help") { PrintHelp(); return 0; }
}

ScenarioConfig config;
if (scenarioPath is not null)
{
    if (!File.Exists(scenarioPath))
    {
        Console.Error.WriteLine($"Scenario file not found: {scenarioPath}");
        return 1;
    }
    try
    {
        var json = await File.ReadAllTextAsync(scenarioPath);
        config = JsonSerializer.Deserialize<ScenarioConfig>(json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            })!;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Failed to parse scenario file: {ex.Message}");
        return 1;
    }
}
else
{
    config = InteractiveScenarioBuilder.Build();
}

try
{
    var result = await useCase.RunAsync(config);
    if (!result.IsSuccess)
    {
        Console.Error.WriteLine("Validation errors:");
        foreach (var err in result.Errors!) Console.Error.WriteLine($"  - {err}");
        return 1;
    }

    reporter.RenderToConsole(result.Result!);

    if (outputPath is not null)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        var mdReporter = new TradingResearchEngine.Infrastructure.Reporting.MarkdownReporter(
            Microsoft.Extensions.Options.Options.Create(
                new TradingResearchEngine.Application.Configuration.ReportingOptions()));
        var md = mdReporter.RenderToMarkdown(result.Result!);
        await File.WriteAllTextAsync(outputPath, md);
        Console.WriteLine($"Report written to {outputPath}");
    }

    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Engine error: {ex.Message}");
    return 2;
}

static void PrintHelp()
{
    Console.WriteLine("TradingResearchEngine CLI");
    Console.WriteLine("Usage: tre [options]");
    Console.WriteLine("  --scenario <path>    Path to scenario JSON file");
    Console.WriteLine("  --output <path>      Path to write Markdown report");
    Console.WriteLine("  --simulations <int>  Number of Monte Carlo simulations");
    Console.WriteLine("  --help               Show this help");
}
