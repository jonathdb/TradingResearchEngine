---
trigger: save
fileMatch: "src/TradingResearchEngine.Core/**/*.cs,src/TradingResearchEngine.Application/**/*.cs"
---

# Test Sync

When a public class or method is added or modified in Core or Application, check whether a corresponding test file exists in `src/TradingResearchEngine.UnitTests/`.

Flag any new public methods that lack test coverage with a reminder to add tests.
