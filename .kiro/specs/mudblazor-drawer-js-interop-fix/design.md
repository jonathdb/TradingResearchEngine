# MudBlazor Drawer JS Interop Fix — Bugfix Design

## Overview

The MudBlazor `MudDrawer` component crashes the Blazor Server circuit on every page load because its `OnAfterRenderAsync` invokes JS interop (`mudElementRef.getBoundingClientRect`) before `MudBlazor.min.js` has finished loading in the browser.

## Root Cause Analysis

The project uses MudBlazor **8.0.0** — the initial v8 release. MudBlazor v8 had significant internal changes (see [v8.0.0 Migration Guide](https://github.com/MudBlazor/MudBlazor/issues/9953)). The `MudDrawer` component calls `UpdateHeightAsync()` in `OnAfterRenderAsync`, which invokes `mudElementRef.getBoundingClientRect` via JS interop. In Blazor Server, this can fire before the MudBlazor JS module has registered its functions.

**Previously attempted fixes that did NOT work:**
1. Reordering script tags — `blazor.web.js` must load first for SignalR
2. `_jsReady` flag deferral in `OnAfterRenderAsync` — MudDrawer's own internal `OnAfterRenderAsync` still fires before JS is ready
3. Enabling prerendering (`prerender: true`) — the interactive re-render still triggers the race condition

**Why upgrading is the correct fix:** MudBlazor has released many patches since 8.0.0 (up to 8.13.x). The JS interop timing issue in `MudDrawer` is a known class of bug that MudBlazor has addressed in subsequent releases. Upgrading to the latest 8.x patch is the proper fix rather than working around an internal MudBlazor defect.

## Fix Strategy (Two-Part)

### Part 1: Upgrade MudBlazor to latest 8.x

Update the `MudBlazor` package reference from `8.0.0` to the latest stable 8.x release. This addresses the root cause — later 8.x releases include fixes for JS interop timing in component lifecycle methods.

### Part 2: Add ErrorBoundary safety net

Wrap the `MudDrawer` in a Blazor `<ErrorBoundary>` with recovery logic as a defense-in-depth measure. If any future JS interop timing issue occurs, the error is caught and recovered from gracefully instead of crashing the circuit.

## Files Changed

| File | Change |
|---|---|
| `src/TradingResearchEngine.Web/TradingResearchEngine.Web.csproj` | Update MudBlazor package version from 8.0.0 to latest 8.x |
| `src/TradingResearchEngine.Web/Components/Layout/MainLayout.razor` | Wrap `MudDrawer` in `<ErrorBoundary>` with auto-recovery |
| `src/TradingResearchEngine.Web/Components/App.razor` | Revert `prerender` back to `false` (the prerender change didn't help and may cause double-render side effects) |

## Correctness Properties

**Property 1 (Fix):** No JSInterop exception crashes the circuit on any page load.

**Property 2 (Preservation):** All MudDrawer functionality (toggle, clip mode, elevation, theme), layout components, and MudBlazor component behavior remain unchanged.

## Testing Strategy

Manual smoke testing:
1. Load the app with cleared browser cache — no crash
2. Refresh pages multiple times — no crash
3. Toggle drawer open/closed — works correctly
4. Navigate between all pages — no crashes
5. Throttle network in DevTools — no crash even with slow loading
