using System.Buffers;
using Microsoft.Extensions.ObjectPool;

namespace TradingResearchEngine.Core.DataHandling;

/// <summary>
/// Object pool for hot-path collections used in DataHandler and Portfolio mark-to-market.
/// Reduces GC pressure during large backtests by pooling <see cref="List{T}"/> instances
/// and providing access to <see cref="ArrayPool{T}"/> for decimal arrays.
/// </summary>
/// <remarks>
/// Transparent to callers — <c>IStrategy.OnMarketData</c> signature remains unchanged.
/// Consumers rent pooled collections for intermediate computations and return them
/// when done, avoiding per-bar allocations on the hot path.
/// </remarks>
public sealed class BarDataPool
{
    private readonly ObjectPool<List<BarRecord>> _barListPool;
    private readonly ArrayPool<decimal> _decimalArrayPool;

    /// <summary>
    /// Initialises a new instance of <see cref="BarDataPool"/> with default pool policies.
    /// </summary>
    public BarDataPool()
    {
        _barListPool = new DefaultObjectPoolProvider()
            .Create(new BarListPooledObjectPolicy());
        _decimalArrayPool = ArrayPool<decimal>.Shared;
    }

    /// <summary>
    /// Rents a pooled <see cref="List{BarRecord}"/> for temporary use.
    /// The list is cleared before being returned to the caller.
    /// </summary>
    /// <returns>A cleared, reusable list instance.</returns>
    public List<BarRecord> RentBarList() => _barListPool.Get();

    /// <summary>
    /// Returns a previously rented <see cref="List{BarRecord}"/> back to the pool.
    /// The list is cleared on return to prevent stale data leakage.
    /// </summary>
    /// <param name="list">The list to return to the pool.</param>
    public void ReturnBarList(List<BarRecord> list) => _barListPool.Return(list);

    /// <summary>
    /// Rents a decimal array of at least <paramref name="minimumLength"/> elements
    /// from the shared <see cref="ArrayPool{T}"/>.
    /// </summary>
    /// <param name="minimumLength">The minimum required array length.</param>
    /// <returns>A decimal array of at least the requested length.</returns>
    public decimal[] RentDecimalArray(int minimumLength) =>
        _decimalArrayPool.Rent(minimumLength);

    /// <summary>
    /// Returns a previously rented decimal array back to the shared pool.
    /// </summary>
    /// <param name="array">The array to return.</param>
    /// <param name="clearArray">When <c>true</c>, zeros the array before returning. Default is <c>false</c>.</param>
    public void ReturnDecimalArray(decimal[] array, bool clearArray = false) =>
        _decimalArrayPool.Return(array, clearArray);

    /// <summary>
    /// Pooled object policy for <see cref="List{BarRecord}"/> that clears lists on return.
    /// </summary>
    private sealed class BarListPooledObjectPolicy : PooledObjectPolicy<List<BarRecord>>
    {
        /// <inheritdoc />
        public override List<BarRecord> Create() => new(capacity: 64);

        /// <inheritdoc />
        public override bool Return(List<BarRecord> obj)
        {
            obj.Clear();
            return true;
        }
    }
}
