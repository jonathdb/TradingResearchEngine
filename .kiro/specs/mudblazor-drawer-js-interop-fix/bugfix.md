# Bugfix Requirements Document

## Introduction

On every page load of the Blazor Server web application (TradingResearchEngine.Web), the MudBlazor `MudDrawer` component throws a `JSInterop` exception that crashes the SignalR circuit:

```
Microsoft.JSInterop.JSException: Could not find 'mudElementRef.getBoundingClientRect' ('mudElementRef' was undefined).
  at MudBlazor.MudDrawer.UpdateHeightAsync()
  at MudBlazor.MudDrawer.OnAfterRenderAsync(Boolean firstRender)
```

This is a timing race condition. `MudDrawer.OnAfterRenderAsync` invokes JS interop to measure element dimensions before the MudBlazor JS module (`MudBlazor.min.js`) has finished loading and registering its functions in the browser. The crash kills the Blazor circuit, requiring a full page refresh. The application is unusable in its current state.

The root cause is that in Blazor Server with `InteractiveServerRenderMode(prerender: false)`, the component lifecycle fires server-side as soon as the SignalR circuit is established, but the browser may not have finished downloading and executing `MudBlazor.min.js` by that point. The `mudElementRef` global is therefore `undefined` when the JS interop call arrives.

## Bug Analysis

### Current Behavior (Defect)

1.1 WHEN the application loads a page containing `MudDrawer` and `MudDrawer.OnAfterRenderAsync` executes before `MudBlazor.min.js` has registered its JS functions in the browser, THEN the system throws a `JSInterop` exception (`mudElementRef was undefined`) and the Blazor circuit crashes.

1.2 WHEN the Blazor circuit crashes due to the JSInterop exception, THEN the system becomes unresponsive and requires a full page refresh to recover, with no graceful degradation or error message shown to the user.

1.3 WHEN the user navigates to any page in the application (since `MudDrawer` is in `MainLayout.razor`), THEN the system is susceptible to the crash on every page load, making the application unreliable.

### Expected Behavior (Correct)

2.1 WHEN the application loads a page containing `MudDrawer` and the MudBlazor JS module has not yet finished loading, THEN the system SHALL defer rendering of the `MudDrawer` (and other JS-dependent MudBlazor components) until the JS runtime is confirmed ready, preventing any JSInterop exception.

2.2 WHEN the MudBlazor JS module finishes loading, THEN the system SHALL render the `MudDrawer` and all deferred MudBlazor layout components normally, with the drawer appearing in its correct open/closed state without visual glitches.

2.3 WHEN the user navigates to any page in the application, THEN the system SHALL load reliably without circuit crashes, regardless of JS module loading timing.

### Unchanged Behavior (Regression Prevention)

3.1 WHEN the MudBlazor JS module is fully loaded and the page renders normally, THEN the system SHALL CONTINUE TO display the `MudDrawer` with its toggle functionality, clip mode, and elevation styling exactly as configured in `MainLayout.razor`.

3.2 WHEN the user clicks the menu icon button to toggle the drawer, THEN the system SHALL CONTINUE TO open and close the drawer via the `@bind-Open="_drawerOpen"` binding.

3.3 WHEN the application loads, THEN the system SHALL CONTINUE TO apply the dark theme, app bar, main content area, nav menu, and execution status bar as defined in `MainLayout.razor`.

3.4 WHEN MudBlazor services are registered via `AddMudServices()` in `Program.cs`, THEN the system SHALL CONTINUE TO use the existing service registration without requiring additional service changes.

3.5 WHEN `blazor.web.js` loads before `MudBlazor.min.js` in `App.razor`, THEN the system SHALL CONTINUE TO maintain this script order since `blazor.web.js` must load first to establish the SignalR connection.

---

### Bug Condition (Formal)

```pascal
FUNCTION isBugCondition(X)
  INPUT: X of type PageLoadEvent
  OUTPUT: boolean

  // The bug triggers when MudDrawer's OnAfterRenderAsync fires
  // before MudBlazor.min.js has registered mudElementRef in the browser
  RETURN X.MudDrawerOnAfterRenderFired AND NOT X.MudBlazorJsModuleReady
END FUNCTION
```

### Fix Checking Property

```pascal
// Property: Fix Checking — MudDrawer deferred until JS ready
FOR ALL X WHERE isBugCondition(X) DO
  result ← loadPage'(X)
  ASSERT no_crash(result) AND no_js_interop_exception(result)
  ASSERT eventually_renders_drawer(result)
END FOR
```

### Preservation Checking Property

```pascal
// Property: Preservation Checking — Normal rendering unchanged
FOR ALL X WHERE NOT isBugCondition(X) DO
  ASSERT loadPage(X) = loadPage'(X)
END FOR
```

This ensures that when the JS module is already loaded (the non-buggy case), the fixed code produces identical behavior to the original — the drawer renders immediately with full functionality.
