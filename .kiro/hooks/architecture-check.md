---
trigger: save
fileMatch: "src/**/*.cs"
---

# Architecture Check

When a C# file is saved in `src/`, verify that it does not violate the dependency rule:

```
Core ← Application ← Infrastructure ← { Cli, Api }
```

Check for:
- Core files importing `TradingResearchEngine.Application` or `TradingResearchEngine.Infrastructure`
- Application files importing `TradingResearchEngine.Infrastructure`
- Any upward layer reference

If a violation is found, warn the developer with the specific file and the offending `using` statement.
