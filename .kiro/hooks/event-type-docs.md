---
trigger: save
fileMatch: "src/TradingResearchEngine.Core/Events/**/*.cs"
---

# Event Type Documentation

When an event type file in `Core/Events/` is saved, verify that all public types and members have XML doc comments (`/// <summary>`).

Flag any public type or member missing documentation.
