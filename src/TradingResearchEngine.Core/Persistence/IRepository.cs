namespace TradingResearchEngine.Core.Persistence;

/// <summary>
/// Generic persistence abstraction. V1 implementation is <c>JsonFileRepository&lt;T&gt;</c>;
/// designed for substitution with a database adapter.
/// </summary>
public interface IRepository<T> where T : IHasId
{
    /// <summary>Persists the entity, overwriting any existing record with the same id.</summary>
    Task SaveAsync(T entity, CancellationToken ct = default);

    /// <summary>Returns the entity with the given id, or <c>null</c> if not found.</summary>
    Task<T?> GetByIdAsync(string id, CancellationToken ct = default);

    /// <summary>Returns all persisted entities.</summary>
    Task<IReadOnlyList<T>> ListAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns the most recent <paramref name="count"/> entities ordered by creation time descending.
    /// Implementations should use a database-level LIMIT where possible.
    /// </summary>
    Task<IReadOnlyList<T>> ListRecentAsync(int count, CancellationToken ct = default);

    /// <summary>
    /// Returns entities whose <c>Status</c> property (serialized as a string) matches
    /// <paramref name="status"/>. Default implementation falls back to <see cref="ListAsync"/>
    /// with an in-memory filter via reflection for backward compatibility.
    /// </summary>
    Task<IReadOnlyList<T>> ListByStatusAsync(string status, CancellationToken ct = default)
    {
        return ListByStatusFallbackAsync(status, ct);
    }

    /// <summary>Default fallback: loads all entities and filters by Status property via reflection.</summary>
    private async Task<IReadOnlyList<T>> ListByStatusFallbackAsync(string status, CancellationToken ct)
    {
        var all = await ListAsync(ct);
        var statusProp = typeof(T).GetProperty("Status");
        if (statusProp is null) return all;

        return all.Where(e =>
        {
            var val = statusProp.GetValue(e);
            return val is not null && string.Equals(val.ToString(), status, StringComparison.OrdinalIgnoreCase);
        }).ToList();
    }

    /// <summary>Deletes the entity with the given id. No-op if not found.</summary>
    Task DeleteAsync(string id, CancellationToken ct = default);
}
