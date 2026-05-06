using TradingResearchEngine.Application.TickImport;

namespace TradingResearchEngine.Infrastructure.TickImport;

/// <summary>Result of downloading a single hour's tick data.</summary>
public sealed record TickDownloadResult(
    DateTime Date, int Hour, IReadOnlyList<TickCsvRow> Ticks);
