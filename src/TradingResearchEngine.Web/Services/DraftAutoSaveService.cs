using Microsoft.Extensions.Logging;
using TradingResearchEngine.Application.Strategies;
using TradingResearchEngine.Core.Persistence;

namespace TradingResearchEngine.Web.Services;

/// <summary>
/// Auto-saves strategy builder drafts with a 3-second debounce timer.
/// Persists <see cref="ConfigDraft"/> via <see cref="IRepository{ConfigDraft}"/>.
/// </summary>
public sealed class DraftAutoSaveService : IDisposable
{
    private readonly IRepository<ConfigDraft> _repository;
    private readonly ILogger<DraftAutoSaveService> _logger;
    private Timer? _debounceTimer;
    private ConfigDraft? _pendingDraft;
    private const int DebounceMs = 3000;

    /// <summary>Timestamp of the last successful auto-save. Null if no save has occurred.</summary>
    public DateTimeOffset? LastSavedAt { get; private set; }

    /// <summary>Event raised when a save completes (success or failure).</summary>
    public event Action<bool>? SaveCompleted;

    /// <summary>Initializes a new instance of <see cref="DraftAutoSaveService"/>.</summary>
    public DraftAutoSaveService(IRepository<ConfigDraft> repository, ILogger<DraftAutoSaveService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Schedules a draft save after the debounce period.
    /// Resets the timer on each call, so rapid changes only trigger one save.
    /// </summary>
    /// <param name="draft">The current draft state to persist.</param>
    public void ScheduleSave(ConfigDraft draft)
    {
        _pendingDraft = draft;
        _debounceTimer?.Dispose();
        _debounceTimer = new Timer(ExecuteSave, null, DebounceMs, Timeout.Infinite);
    }

    private async void ExecuteSave(object? state)
    {
        var draft = _pendingDraft;
        if (draft is null) return;

        try
        {
            await _repository.SaveAsync(draft);
            LastSavedAt = DateTimeOffset.UtcNow;
            _logger.LogDebug("Draft auto-saved: {DraftId} at {Timestamp}", draft.DraftId, LastSavedAt);
            SaveCompleted?.Invoke(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Draft auto-save failed for {DraftId}", draft.DraftId);
            SaveCompleted?.Invoke(false);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _debounceTimer?.Dispose();
    }
}
