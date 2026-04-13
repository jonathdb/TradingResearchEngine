# Implementation Plan — MudBlazor Drawer JS Interop Fix

- [x] 1. Upgrade MudBlazor package to latest 8.x

  - [x] 1.1 Update MudBlazor package reference in Web project

    - Change `<PackageReference Include="MudBlazor" Version="8.0.0" />` to the latest stable 8.x version
    - Run `dotnet restore` and `dotnet build` to verify no breaking API changes
    - Check the [v8.0.0 Migration Guide](https://github.com/MudBlazor/MudBlazor/issues/9953) for any API changes that affect existing Razor components
    - _Bugfix Requirements: 2.1, 2.2, 2.3_

  - [x] 1.2 Fix any compilation errors from MudBlazor API changes

    - Review build output for errors in Razor components that use MudBlazor
    - Apply any required API migrations per the migration guide
    - _Bugfix Requirements: 3.1, 3.2, 3.3_

- [x] 2. Revert prerender change in App.razor

  - [x] 2.1 Set `prerender` back to `false` on both render mode declarations

    - Change `InteractiveServerRenderMode(prerender: true)` back to `InteractiveServerRenderMode(prerender: false)` on both `HeadOutlet` and `Routes` in `App.razor`
    - The prerender change was attempted as a fix but did not resolve the issue and may cause double-render side effects
    - _Bugfix Requirements: 3.5_

- [x] 3. Add ErrorBoundary safety net around MudDrawer

  - [x] 3.1 Wrap MudDrawer in an ErrorBoundary with auto-recovery in MainLayout.razor

    - Wrap the `<MudDrawer>` block in `<ErrorBoundary @ref="_errorBoundary">`
    - In `OnAfterRenderAsync`, call `_errorBoundary?.Recover()` to auto-recover from any transient JS interop errors
    - The `<ChildContent>` renders the drawer normally; the `<ErrorContent>` renders a minimal fallback (e.g. just the `<NavMenu />` without the drawer wrapper)
    - _Bugfix Requirements: 2.1, 3.1, 3.2_

- [-] 4. Verify the fix

  - [x] 4.1 Build the solution and confirm zero errors

    - Run `dotnet build` on the full solution
    - Confirm zero warnings related to MudBlazor
    - _Bugfix Requirements: 3.3, 3.4_

  - [ ] 4.2 Run the web application and confirm no circuit crash

    - Start the app with `dotnet run --project src/TradingResearchEngine.Web`
    - Load the app in a browser — confirm no `mudElementRef.getBoundingClientRect` error in server logs
    - Refresh the page multiple times — confirm consistent stability
    - Toggle the drawer open/closed — confirm it works
    - Navigate between pages — confirm no crashes
    - _Bugfix Requirements: 2.1, 2.2, 2.3, 3.1, 3.2, 3.3_
