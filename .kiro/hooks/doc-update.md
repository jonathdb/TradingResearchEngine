---
trigger: save
fileMatch: "src/TradingResearchEngine.Core/**/*.cs,src/TradingResearchEngine.Application/**/*.cs"
---

# Documentation Update

When a public API in Core or Application changes (new public type, renamed method, changed signature), check if the corresponding documentation in `docs/` needs updating.

Suggest updates to the relevant doc file if the change affects documented behaviour.
