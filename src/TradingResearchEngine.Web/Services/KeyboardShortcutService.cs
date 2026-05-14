using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace TradingResearchEngine.Web.Services;

/// <summary>
/// Represents a registered keyboard shortcut with its key combination, description, and action.
/// </summary>
public sealed record ShortcutRegistration(
    string Key,
    string Description,
    string Category,
    Func<Task> Action);

/// <summary>
/// Manages global keyboard shortcuts via JS interop.
/// Supports registering/unregistering shortcuts, fuzzy search for command palette,
/// and skips shortcuts when focus is inside text inputs.
/// </summary>
public sealed class KeyboardShortcutService : IAsyncDisposable
{
    private readonly IJSRuntime _jsRuntime;
    private readonly NavigationManager _navigation;
    private readonly Dictionary<string, ShortcutRegistration> _shortcuts = new(StringComparer.OrdinalIgnoreCase);
    private DotNetObjectReference<KeyboardShortcutService>? _dotNetRef;
    private bool _initialized;
    private bool _commandPaletteVisible;

    /// <summary>Event raised when the command palette visibility changes.</summary>
    public event Action<bool>? CommandPaletteVisibilityChanged;

    /// <summary>Gets whether the command palette is currently visible.</summary>
    public bool IsCommandPaletteVisible => _commandPaletteVisible;

    /// <summary>Gets all registered shortcuts.</summary>
    public IReadOnlyDictionary<string, ShortcutRegistration> Shortcuts => _shortcuts;

    /// <summary>Initializes a new instance of <see cref="KeyboardShortcutService"/>.</summary>
    public KeyboardShortcutService(IJSRuntime jsRuntime, NavigationManager navigation)
    {
        _jsRuntime = jsRuntime;
        _navigation = navigation;
    }

    /// <summary>
    /// Initializes the JS interop listener. Must be called after the component has rendered.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _dotNetRef = DotNetObjectReference.Create(this);
        await _jsRuntime.InvokeVoidAsync("keyboardShortcuts.init", _dotNetRef);
        _initialized = true;
        RegisterDefaultShortcuts();
    }

    /// <summary>
    /// Registers a keyboard shortcut.
    /// </summary>
    /// <param name="key">The key combination (e.g., "Ctrl+K", "Escape").</param>
    /// <param name="description">Human-readable description of the shortcut.</param>
    /// <param name="category">Category for grouping in the help overlay.</param>
    /// <param name="action">Async action to execute when the shortcut is triggered.</param>
    public void Register(string key, string description, string category, Func<Task> action)
    {
        _shortcuts[key] = new ShortcutRegistration(key, description, category, action);
    }

    /// <summary>
    /// Unregisters a keyboard shortcut by its key combination.
    /// </summary>
    public void Unregister(string key)
    {
        _shortcuts.Remove(key);
    }

    /// <summary>
    /// Performs fuzzy search across registered shortcuts.
    /// Returns shortcuts whose description or key contains the query as a substring (case-insensitive).
    /// </summary>
    public IReadOnlyList<ShortcutRegistration> FuzzySearch(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return _shortcuts.Values.ToList();

        var lowerQuery = query.ToLowerInvariant();
        return _shortcuts.Values
            .Where(s => s.Description.Contains(lowerQuery, StringComparison.OrdinalIgnoreCase)
                     || s.Key.Contains(lowerQuery, StringComparison.OrdinalIgnoreCase)
                     || s.Category.Contains(lowerQuery, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Sets the command palette visibility state.
    /// </summary>
    public void SetCommandPaletteVisible(bool visible)
    {
        _commandPaletteVisible = visible;
        CommandPaletteVisibilityChanged?.Invoke(visible);
    }

    /// <summary>
    /// JS interop callback invoked on keydown events.
    /// Returns true if the shortcut was handled (to prevent default browser behaviour).
    /// </summary>
    [JSInvokable]
    public async Task<bool> OnKeyDown(string shortcutKey)
    {
        if (_shortcuts.TryGetValue(shortcutKey, out var registration))
        {
            await registration.Action();
            return true;
        }
        return false;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_initialized)
        {
            try
            {
                await _jsRuntime.InvokeVoidAsync("keyboardShortcuts.dispose");
            }
            catch (JSDisconnectedException)
            {
                // Circuit disconnected, safe to ignore
            }
        }
        _dotNetRef?.Dispose();
    }

    private void RegisterDefaultShortcuts()
    {
        Register("Ctrl+k", "Command Palette", "Navigation", () =>
        {
            SetCommandPaletteVisible(!_commandPaletteVisible);
            return Task.CompletedTask;
        });

        Register("Ctrl+n", "New Strategy", "Navigation", () =>
        {
            _navigation.NavigateTo("/strategy-builder");
            return Task.CompletedTask;
        });

        Register("Ctrl+r", "Re-run Last Backtest", "Actions", () =>
        {
            // Placeholder — actual re-run logic depends on state management
            return Task.CompletedTask;
        });

        Register("Escape", "Close Dialog / Panel", "General", () =>
        {
            if (_commandPaletteVisible)
                SetCommandPaletteVisible(false);
            return Task.CompletedTask;
        });
    }
}
