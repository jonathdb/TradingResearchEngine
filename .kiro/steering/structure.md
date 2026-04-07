# Solution Structure

## Layout

All projects live under `src/`. The solution file sits at the repository root.

```
TradingResearchEngine.sln
src/
  TradingResearchEngine.Core/
  TradingResearchEngine.Application/
  TradingResearchEngine.Infrastructure/
  TradingResearchEngine.Cli/
  TradingResearchEngine.Api/
  TradingResearchEngine.UnitTests/
  TradingResearchEngine.IntegrationTests/
docs/
.kiro/
```

## Dependency Rule

```
Core ← Application ← Infrastructure ← { Cli, Api }
```

- Core has zero references to any other project in this solution.
- Application references Core only.
- Infrastructure references Application and Core.
- Cli and Api reference Infrastructure and Application (never Core directly for orchestration).
- UnitTests references Core and Application only — never Infrastructure.
- IntegrationTests may reference all projects.

Violations of this rule are caught by the `architecture-check` hook.

## Folder Conventions Within Each Project

Follow the folder layout defined in the design document exactly.
Do not create top-level folders not present in the design without updating the design first.
