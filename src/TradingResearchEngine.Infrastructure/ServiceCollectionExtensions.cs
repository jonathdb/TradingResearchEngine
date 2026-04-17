using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.DataHandling;
using TradingResearchEngine.Core.Persistence;
using TradingResearchEngine.Core.Reporting;
using TradingResearchEngine.Core.Results;
using TradingResearchEngine.Infrastructure.DataProviders;
using TradingResearchEngine.Infrastructure.Persistence;
using TradingResearchEngine.Infrastructure.Reporting;
using TradingResearchEngine.Application.PropFirm;
using TradingResearchEngine.Application.Research;

namespace TradingResearchEngine.Infrastructure;

/// <summary>Registers all Infrastructure services.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Infrastructure services: data providers, reporters, repository, and configuration bindings.
    /// </summary>
    public static IServiceCollection AddTradingResearchEngineInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RepositoryOptions>(configuration.GetSection("Repository"));
        services.Configure<ReportingOptions>(configuration.GetSection("Reporting"));

        // HTTP client factory for Yahoo Finance and HTTP REST data providers
        services.AddHttpClient();

        // Data provider factory — resolves providers from ScenarioConfig at runtime
        services.AddSingleton<IDataProviderFactory, DataProviderFactory>();

        // Default data provider for DI consumers that don't use the factory
        services.AddTransient<IDataProvider>(sp =>
        {
            var dataPath = configuration["DataProvider:FilePath"] ?? "data.csv";
            var logger = sp.GetRequiredService<ILogger<CsvDataProvider>>();
            return new CsvDataProvider(dataPath, logger);
        });

        services.AddSingleton<IReporter, ConsoleReporter>();
        services.AddSingleton<DataFileService>();
        services.AddSingleton<IRepository<BacktestResult>, JsonFileRepository<BacktestResult>>();
        // V6: SQLite-indexed backtest result repository
        services.AddSingleton<IBacktestResultRepository, SqliteIndexRepository>();
        services.AddSingleton<IRepository<ScenarioConfig>, JsonFileRepository<ScenarioConfig>>();
        services.AddSingleton<IRepository<TradingResearchEngine.Application.PropFirm.FirmRuleSet>,
            JsonFileRepository<TradingResearchEngine.Application.PropFirm.FirmRuleSet>>();

        // V5: Job, ConfigDraft, and ConfigPreset repositories
        services.AddSingleton<IRepository<TradingResearchEngine.Application.Research.BacktestJob>,
            JsonFileRepository<TradingResearchEngine.Application.Research.BacktestJob>>();
        services.AddSingleton<IRepository<TradingResearchEngine.Application.Strategy.ConfigDraft>,
            JsonFileRepository<TradingResearchEngine.Application.Strategy.ConfigDraft>>();
        services.AddSingleton<IRepository<TradingResearchEngine.Application.Strategy.ConfigPreset>,
            JsonFileRepository<TradingResearchEngine.Application.Strategy.ConfigPreset>>();

        // V3: Strategy and study repositories
        services.AddSingleton<TradingResearchEngine.Application.Strategy.IStrategyRepository>(sp =>
        {
            var baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TradingResearchEngine", "Strategies");
            return new JsonStrategyRepository(baseDir);
        });

        services.AddSingleton<TradingResearchEngine.Application.Research.IStudyRepository>(sp =>
        {
            var baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TradingResearchEngine", "Studies");
            return new JsonStudyRepository(baseDir);
        });

        // V3: Settings service
        services.AddSingleton(sp =>
        {
            var settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TradingResearchEngine", "settings.json");
            return new Settings.SettingsService(settingsPath);
        });

        // V4: Data file repository
        services.AddSingleton<TradingResearchEngine.Application.DataFiles.IDataFileRepository>(sp =>
        {
            var baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TradingResearchEngine", "DataFiles");
            return new JsonDataFileRepository(baseDir);
        });

        // V4: Report exporter
        services.AddSingleton<TradingResearchEngine.Application.Export.IReportExporter>(sp =>
        {
            var exportDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TradingResearchEngine", "Exports");
            return new Export.ReportExporter(exportDir);
        });

        // V4: Migration service
        services.AddSingleton<MigrationService>();

        // V6: Prop-firm evaluation repository
        services.AddSingleton<IPropFirmEvaluationRepository>(sp =>
        {
            var baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TradingResearchEngine", "PropFirmEvaluations");
            return new JsonPropFirmEvaluationRepository(baseDir);
        });

        // V6: Prop-firm pack loader
        services.AddSingleton<IPropFirmPackLoader>(sp =>
        {
            var firmsDir = Path.Combine(Directory.GetCurrentDirectory(), "data", "firms");
            return new PropFirm.JsonPropFirmPackLoader(firmsDir);
        });

        // Market Data: import repository
        services.AddSingleton<TradingResearchEngine.Application.MarketData.IMarketDataImportRepository>(sp =>
        {
            var baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TradingResearchEngine", "Imports");
            return new MarketData.JsonMarketDataImportRepository(baseDir);
        });

        // Market Data: Dukascopy provider
        services.AddSingleton<TradingResearchEngine.Application.MarketData.IMarketDataProvider>(sp =>
        {
            var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
            var logger = sp.GetRequiredService<ILogger<MarketData.DukascopyImportProvider>>();
            return new MarketData.DukascopyImportProvider(httpFactory.CreateClient(), logger);
        });

        // Market Data: import service
        services.AddSingleton(sp =>
        {
            var importRepo = sp.GetRequiredService<TradingResearchEngine.Application.MarketData.IMarketDataImportRepository>();
            var dataFileRepo = sp.GetRequiredService<TradingResearchEngine.Application.DataFiles.IDataFileRepository>();
            var providers = sp.GetServices<TradingResearchEngine.Application.MarketData.IMarketDataProvider>();
            var logger = sp.GetRequiredService<ILogger<TradingResearchEngine.Application.MarketData.MarketDataImportService>>();
            var dataDir = sp.GetRequiredService<DataFileService>().DataDirectory;
            return new TradingResearchEngine.Application.MarketData.MarketDataImportService(
                importRepo, dataFileRepo, providers, logger, dataDir);
        });

        // V3: Strategy templates
        services.AddSingleton<IReadOnlyList<TradingResearchEngine.Application.Strategy.StrategyTemplate>>(
            TradingResearchEngine.Application.Strategy.DefaultStrategyTemplates.All);

        return services;
    }
}
