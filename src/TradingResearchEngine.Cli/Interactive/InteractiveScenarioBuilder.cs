using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Engine;

namespace TradingResearchEngine.Cli.Interactive;

/// <summary>
/// Guides the user through scenario selection and parameter entry via console prompts.
/// </summary>
public static class InteractiveScenarioBuilder
{
    /// <summary>Builds a ScenarioConfig from interactive console input.</summary>
    public static ScenarioConfig Build()
    {
        Console.Write("Scenario ID: ");
        var scenarioId = Console.ReadLine() ?? "interactive-run";

        Console.Write("Description: ");
        var description = Console.ReadLine() ?? "";

        Console.Write("Strategy type: ");
        var strategyType = Console.ReadLine() ?? "";

        Console.Write("Data provider type: ");
        var dataProviderType = Console.ReadLine() ?? "csv";

        Console.Write("Initial cash (default 100000): ");
        var cashInput = Console.ReadLine();
        var initialCash = decimal.TryParse(cashInput, out var cash) ? cash : 100_000m;

        Console.Write("Replay mode (Bar/Tick, default Bar): ");
        var modeInput = Console.ReadLine();
        var replayMode = modeInput?.Equals("Tick", StringComparison.OrdinalIgnoreCase) == true
            ? ReplayMode.Tick : ReplayMode.Bar;

        return new ScenarioConfig(
            scenarioId, description, replayMode,
            dataProviderType, new Dictionary<string, object>(),
            strategyType, new Dictionary<string, object>(),
            new Dictionary<string, object>(),
            "ZeroSlippageModel", "ZeroCommissionModel",
            initialCash, 0.02m, null, null, null, null);
    }
}
