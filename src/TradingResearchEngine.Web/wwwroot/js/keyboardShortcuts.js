/**
 * Keyboard shortcut system for TradingResearchEngine.
 * Listens to document keydown events and invokes .NET callbacks via JS interop.
 * Skips shortcuts when focus is inside text inputs to avoid interfering with typing.
 */
window.keyboardShortcuts = {
    _dotNetRef: null,
    _listener: null,

    /**
     * Initializes the keyboard shortcut listener.
     * @param {object} dotNetRef - .NET object reference for invoking callbacks.
     */
    init: function (dotNetRef) {
        this._dotNetRef = dotNetRef;
        this._listener = this._handleKeyDown.bind(this);
        document.addEventListener('keydown', this._listener);
    },

    /**
     * Removes the keyboard shortcut listener and cleans up.
     */
    dispose: function () {
        if (this._listener) {
            document.removeEventListener('keydown', this._listener);
            this._listener = null;
        }
        this._dotNetRef = null;
    },

    /**
     * Handles keydown events, skipping when focus is in text inputs.
     * @param {KeyboardEvent} e
     */
    _handleKeyDown: function (e) {
        if (!this._dotNetRef) return;

        // Skip if focus is inside a text input, textarea, or contenteditable element
        var tag = e.target.tagName;
        if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return;
        if (e.target.isContentEditable) return;

        // Do not interfere with browser-native shortcuts
        if ((e.ctrlKey || e.metaKey) && ['c', 'v', 'x', 'a', 't', 'w', 'f', 'z', 'y'].includes(e.key.toLowerCase())) {
            // Allow Ctrl+K and Ctrl+N and Ctrl+R to be captured
            if (!['k', 'n', 'r'].includes(e.key.toLowerCase())) return;
        }

        // Build shortcut key string
        var parts = [];
        if (e.ctrlKey || e.metaKey) parts.push('Ctrl');
        if (e.altKey) parts.push('Alt');
        if (e.shiftKey) parts.push('Shift');
        parts.push(e.key);
        var shortcutKey = parts.join('+');

        // Invoke .NET handler
        this._dotNetRef.invokeMethodAsync('OnKeyDown', shortcutKey).then(function (handled) {
            if (handled) {
                e.preventDefault();
                e.stopPropagation();
            }
        });
    }
};
