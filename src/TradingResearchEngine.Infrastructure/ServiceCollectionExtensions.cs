using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingResearchEngine.Application.AI;
using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Application.TickImport;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.DataHandling;
using TradingResearchEngine.Core.Persistence;
using TradingResearchEngine.Core.Reporting;
using TradingResearchEngine.Core.Results;
using TradingResearchEngine.Infrastructure.AI;
using TradingResearchEngine.Infrastructure.DataProviders;
using TradingResearchEngine.Infrastructure.Persistence;
using TradingResearchEngine.Infrastructure.Reporting;
using TradingResearchEngine.Infrastructure.TickImport;
using TradingResearchEngine.Application.Export;
using TradingResearchEngine.Application.PaperTrading;
using TradingResearchEngine.Application.PropFirm;
using TradingResearchEngine.Application.Research;
using TradingResearchEngine.Infrastructure.Export;

namespace TradingResearchEngine.Infrastructure;

/// <summary>Registers all Infrastructure services.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Returns a project-relative path under <c>./data/{subfolder}</c>.
    /// </summary>
    private static string DataSubDir(string subfolder)
        => Path.Combine(Directory.GetCurrentDirectory(), "data", subfolder);

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
        services.AddSingleton(sp =>
        {
            var settingsService = sp.GetRequiredService<Settings.SettingsService>();
            var settings = settingsService.Load();
            return new DataFileService(settings.DataDirectory, settings.QdmWatchDirectory);
        });
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
            return new JsonStrategyRepository(DataSubDir("strategies"));
        });

        services.AddSingleton<TradingResearchEngine.Application.Research.IStudyRepository>(sp =>
        {
            return new JsonStudyRepository(DataSubDir("studies"));
        });

        // V3: Settings service
        services.AddSingleton(sp =>
        {
            var settingsPath = Path.Combine(Directory.GetCurrentDirectory(), "data", "settings.json");
            return new Settings.SettingsService(settingsPath);
        });

        // V4: Data file repository
        services.AddSingleton<TradingResearchEngine.Application.DataFiles.IDataFileRepository>(sp =>
        {
            return new JsonDataFileRepository(DataSubDir("datafiles"));
        });

        // V4: Report exporter
        services.AddSingleton<TradingResearchEngine.Application.Export.IReportExporter>(sp =>
        {
            return new Export.ReportExporter(DataSubDir("exports"));
        });

        // V4: Migration service
        services.AddSingleton<MigrationService>();

        // V7: Prompt loader — reads .md files from Prompts/ directory at startup
        services.AddSingleton<IPromptLoader>(sp =>
        {
            var promptsDir = Path.Combine(Directory.GetCurrentDirectory(), "Prompts");
            return new PromptLoader(promptsDir);
        });

        // V7: LLM provider configuration
        services.Configure<LlmProviderOptions>(configuration.GetSection("LlmProvider"));

        // V7: Individual LLM provider translators (keyed for fallback chain assembly)
        services.AddSingleton<GoogleAiStudioTranslator>();
        services.AddSingleton<GroqTranslator>();
        services.AddSingleton<OllamaTranslator>();

        // V7: Fallback strategy idea translator — registered as IStrategyIdeaTranslator
        services.AddSingleton<IStrategyIdeaTranslator>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<LlmProviderOptions>>();
            var logger = sp.GetRequiredService<ILogger<FallbackStrategyIdeaTranslator>>();
            var config = options.Value;

            // Build provider chain based on configuration order
            var providers = new List<IStrategyIdeaTranslator>();

            // Add primary provider first
            AddProviderByName(sp, providers, config.Provider);

            // Add fallback providers in configured order
            foreach (var fallback in config.FallbackProviders)
            {
                AddProviderByName(sp, providers, fallback);
            }

            return new FallbackStrategyIdeaTranslator(providers, options, logger);
        });

        // V6: Prop-firm evaluation repository
        services.AddSingleton<IPropFirmEvaluationRepository>(sp =>
        {
            return new JsonPropFirmEvaluationRepository(DataSubDir("prop-firm-evaluations"));
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
            return new MarketData.JsonMarketDataImportRepository(DataSubDir("imports"));
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

        // Tick Import: configuration binding
        services.Configure<TickImportOptions>(configuration.GetSection(TickImportOptions.SectionName));

        // Tick Import: cache service
        services.AddSingleton<ITickCacheService, TickCacheService>();

        // Tick Import: repositories
        services.AddSingleton<ITickImportRepository>(sp =>
        {
            return new JsonTickImportRepository(
                Path.Combine(Directory.GetCurrentDirectory(), "data", "tick-imports"));
        });
        services.AddSingleton<IGeneratedTimeframeRepository>(sp =>
        {
            return new JsonGeneratedTimeframeRepository(
                Path.Combine(Directory.GetCurrentDirectory(), "data", "generated-timeframes"));
        });

        // Tick Import: downloader with named HttpClient
        services.AddHttpClient("DukascopyTick", (sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<TickImportOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(60);
        }).ConfigurePrimaryHttpMessageHandler(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<TickImportOptions>>().Value;
            return new SocketsHttpHandler
            {
                MaxConnectionsPerServer = opts.MaxConnectionsPerServer
            };
        });

        services.AddSingleton<ITickDownloader>(sp =>
        {
            var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpFactory.CreateClient("DukascopyTick");
            var logger = sp.GetRequiredService<ILogger<DukascopyTickDownloader>>();
            var options = sp.GetRequiredService<IOptions<TickImportOptions>>();
            return new DukascopyTickDownloader(httpClient, logger, options);
        });

        // Tick Import: application services
        services.AddSingleton<TickImportService>();
        services.AddSingleton<TimeframeGeneratorService>();

        // V8: Strategy exporters (keyed by format)
        services.AddSingleton<IStrategyExporter, MQL4StrategyExporter>();
        services.AddSingleton<IStrategyExporter, MQL5StrategyExporter>();
        services.AddSingleton<IStrategyExporter, PineScriptExporter>();

        // V8: Streaming data provider (wraps the default IDataProvider)
        services.AddSingleton<IStreamingDataProvider>(sp =>
        {
            var inner = sp.GetRequiredService<IDataProvider>();
            var logger = sp.GetRequiredService<ILogger<PollingStreamingDataProvider>>();
            return new PollingStreamingDataProvider(inner, TimeSpan.FromSeconds(1), 1.0, logger);
        });

        // V8: AI Strategy Assistant — override Application's disabled fallback when API key is configured
        services.AddSingleton<IGeminiClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<GeminiOptions>>().Value;
            if (string.IsNullOrWhiteSpace(options.ApiKey))
            {
                // Return a no-op client that will never be called (assistant is disabled)
                return new NoOpGeminiClient();
            }
            var logger = sp.GetRequiredService<ILogger<GeminiClient>>();
            return new GeminiClient(Options.Create(options), logger);
        });

        // Override the Application-layer disabled fallback with the real implementation when API key is present
        services.AddSingleton<IAIStrategyAssistant>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<GeminiOptions>>().Value;
            var logger = sp.GetRequiredService<ILogger<GeminiStrategyAssistant>>();

            if (string.IsNullOrWhiteSpace(options.ApiKey))
            {
                logger.LogWarning("Gemini API key is not configured. AI strategy assistant features are disabled.");
                return new DisabledInfrastructureAIAssistant();
            }

            var registry = sp.GetRequiredService<TradingResearchEngine.Application.Strategy.StrategyRegistry>();
            var geminiClient = sp.GetRequiredService<IGeminiClient>();
            return new GeminiStrategyAssistant(
                Options.Create(options), registry, logger, geminiClient);
        });

        // V8: Paper session record repository
        services.AddSingleton<IRepository<PaperSessionRecord>,
            JsonFileRepository<PaperSessionRecord>>();

        return services;
    }

    /// <summary>
    /// Resolves an LLM provider by name and adds it to the provider chain.
    /// </summary>
    private static void AddProviderByName(
        IServiceProvider sp,
        List<IStrategyIdeaTranslator> providers,
        string providerName)
    {
        IStrategyIdeaTranslator? provider = providerName switch
        {
            "GoogleAIStudio" => sp.GetRequiredService<GoogleAiStudioTranslator>(),
            "Groq" => sp.GetRequiredService<GroqTranslator>(),
            "Ollama" => sp.GetRequiredService<OllamaTranslator>(),
            _ => null
        };

        if (provider is not null)
        {
            providers.Add(provider);
        }
    }

    /// <summary>No-op Gemini client for when API key is not configured.</summary>
    private sealed class NoOpGeminiClient : IGeminiClient
    {
        public Task<string> GenerateJsonAsync(string systemPrompt, string userMessage, CancellationToken ct)
            => throw new InvalidOperationException("Gemini client is not configured.");
    }

    /// <summary>Disabled AI assistant fallback for Infrastructure layer.</summary>
    private sealed class DisabledInfrastructureAIAssistant : IAIStrategyAssistant
    {
        public Task<Application.AI.AIStrategyDraft> GenerateStrategyAsync(string naturalLanguagePrompt, CancellationToken ct)
            => throw new InvalidOperationException(
                "AI strategy assistant is disabled. Configure a valid Gemini API key in GeminiOptions to enable this feature.");

        public Task<Application.AI.AIStrategyDraft> RefineStrategyAsync(
            Application.AI.AIStrategyDraft current, Core.Results.BacktestResult lastResult,
            string refinementPrompt, CancellationToken ct)
            => throw new InvalidOperationException(
                "AI strategy assistant is disabled. Configure a valid Gemini API key in GeminiOptions to enable this feature.");
    }
}
