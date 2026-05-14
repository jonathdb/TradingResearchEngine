namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Persistence interface for research journal entries.
/// </summary>
public interface IResearchJournalRepository
{
    /// <summary>Lists all journal entries for a strategy.</summary>
    Task<IReadOnlyList<ResearchJournalEntry>> ListByStrategyAsync(string strategyId, CancellationToken ct = default);

    /// <summary>Lists journal entries within a date range.</summary>
    Task<IReadOnlyList<ResearchJournalEntry>> ListByDateRangeAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    /// <summary>Saves a journal entry.</summary>
    Task SaveAsync(ResearchJournalEntry entry, CancellationToken ct = default);
}
