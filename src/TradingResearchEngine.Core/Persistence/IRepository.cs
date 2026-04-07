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

    /// <summary>Deletes the entity with the given id. No-op if not found.</summary>
    Task DeleteAsync(string id, CancellationToken ct = default);
}
