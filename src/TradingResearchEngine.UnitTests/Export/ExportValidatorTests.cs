using TradingResearchEngine.Application.Export;

namespace TradingResearchEngine.UnitTests.Export;

/// <summary>
/// Regression test fixtures for ExportValidator covering known-good and known-bad
/// export patterns for Pine Script and MQL formats.
/// </summary>
public sealed class ExportValidatorTests
{
    private readonly ExportValidator _sut = new();

    #region Empty/Null Input

    [Fact]
    public void Validate_EmptyCode_ReturnsFailure()
    {
        var result = _sut.Validate("", ExportFormat.PineScript);

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Equal("content", result.Errors[0].Section);
    }

    [Fact]
    public void Validate_WhitespaceOnlyCode_ReturnsFailure()
    {
        var result = _sut.Validate("   \n\t  \n  ", ExportFormat.MQL4);

        Assert.False(result.IsValid);
        Assert.Equal("content", result.Errors[0].Section);
    }

    #endregion

    #region Pine Script — Known-Good Patterns

    [Fact]
    public void Validate_PineScript_ValidMinimalStrategy_ReturnsSuccess()
    {
        const string code = """
            //@version=5
            strategy("My Strategy", overlay=true)

            longCondition = ta.crossover(ta.sma(close, 14), ta.sma(close, 28))
            if (longCondition)
                strategy.entry("Long", strategy.long)
            """;

        var result = _sut.Validate(code, ExportFormat.PineScript);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_PineScript_ValidVersion6_ReturnsSuccess()
    {
        const string code = """
            //@version=6
            strategy("Donchian Breakout", overlay=true)

            length = input.int(20, "Channel Length")
            upper = ta.highest(high, length)
            lower = ta.lowest(low, length)

            if (close > upper[1])
                strategy.entry("Long", strategy.long)
            if (close < lower[1])
                strategy.close("Long")
            """;

        var result = _sut.Validate(code, ExportFormat.PineScript);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_PineScript_WithBracesInStrings_ReturnsSuccess()
    {
        const string code = """
            //@version=5
            strategy("Test {braces} in strings", overlay=true)

            label.new(bar_index, high, "Price: {close}")
            """;

        var result = _sut.Validate(code, ExportFormat.PineScript);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    #endregion

    #region Pine Script — Known-Bad Patterns

    [Fact]
    public void Validate_PineScript_MissingVersionDirective_ReportsError()
    {
        const string code = """
            strategy("My Strategy", overlay=true)

            longCondition = ta.crossover(ta.sma(close, 14), ta.sma(close, 28))
            if (longCondition)
                strategy.entry("Long", strategy.long)
            """;

        var result = _sut.Validate(code, ExportFormat.PineScript);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Section == "version directive");
    }

    [Fact]
    public void Validate_PineScript_MissingStrategyDeclaration_ReportsError()
    {
        const string code = """
            //@version=5

            longCondition = ta.crossover(ta.sma(close, 14), ta.sma(close, 28))
            plot(close)
            """;

        var result = _sut.Validate(code, ExportFormat.PineScript);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Section == "strategy declaration");
    }

    [Fact]
    public void Validate_PineScript_InvalidVersionNumber_ReportsError()
    {
        const string code = """
            //@version=99
            strategy("My Strategy", overlay=true)
            """;

        var result = _sut.Validate(code, ExportFormat.PineScript);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e =>
            e.Section == "version directive" && e.Line == 1);
    }

    [Fact]
    public void Validate_PineScript_UnmatchedOpeningBrace_ReportsError()
    {
        const string code = """
            //@version=5
            strategy("My Strategy", overlay=true)

            if (close > open) {
                strategy.entry("Long", strategy.long)
            """;

        var result = _sut.Validate(code, ExportFormat.PineScript);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Section == "braces");
    }

    [Fact]
    public void Validate_PineScript_UnmatchedClosingBrace_ReportsError()
    {
        const string code = """
            //@version=5
            strategy("My Strategy", overlay=true)

            if (close > open)
                strategy.entry("Long", strategy.long)
            }
            """;

        var result = _sut.Validate(code, ExportFormat.PineScript);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Section == "braces");
    }

    [Fact]
    public void Validate_PineScript_MissingBothRequiredSections_ReportsMultipleErrors()
    {
        const string code = """
            // This is just a comment
            plot(close)
            """;

        var result = _sut.Validate(code, ExportFormat.PineScript);

        Assert.False(result.IsValid);
        Assert.True(result.Errors.Count >= 2);
        Assert.Contains(result.Errors, e => e.Section == "version directive");
        Assert.Contains(result.Errors, e => e.Section == "strategy declaration");
    }

    #endregion

    #region MQL4 — Known-Good Patterns

    [Fact]
    public void Validate_MQL4_ValidMinimalEA_ReturnsSuccess()
    {
        const string code = """
            //+------------------------------------------------------------------+
            //| Expert initialization function                                     |
            //+------------------------------------------------------------------+
            int OnInit()
            {
                return(INIT_SUCCEEDED);
            }

            //+------------------------------------------------------------------+
            //| Expert tick function                                               |
            //+------------------------------------------------------------------+
            void OnTick()
            {
                double ma = iMA(NULL, 0, 14, 0, MODE_SMA, PRICE_CLOSE, 0);
                if (Close[0] > ma)
                {
                    OrderSend(Symbol(), OP_BUY, 0.1, Ask, 3, 0, 0, "Buy", 0, 0, Green);
                }
            }
            """;

        var result = _sut.Validate(code, ExportFormat.MQL4);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    #endregion

    #region MQL5 — Known-Good Patterns

    [Fact]
    public void Validate_MQL5_ValidMinimalEA_ReturnsSuccess()
    {
        const string code = """
            //+------------------------------------------------------------------+
            //| Expert initialization function                                     |
            //+------------------------------------------------------------------+
            int OnInit()
            {
                return(INIT_SUCCEEDED);
            }

            //+------------------------------------------------------------------+
            //| Expert tick function                                               |
            //+------------------------------------------------------------------+
            void OnTick()
            {
                double maArray[];
                int maHandle = iMA(_Symbol, PERIOD_CURRENT, 14, 0, MODE_SMA, PRICE_CLOSE);
                CopyBuffer(maHandle, 0, 0, 1, maArray);

                if (SymbolInfoDouble(_Symbol, SYMBOL_BID) > maArray[0])
                {
                    // Open buy position
                }
            }
            """;

        var result = _sut.Validate(code, ExportFormat.MQL5);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_MQL5_WithNestedBraces_ReturnsSuccess()
    {
        const string code = """
            int OnInit()
            {
                if (true)
                {
                    for (int i = 0; i < 10; i++)
                    {
                        Print("test");
                    }
                }
                return(INIT_SUCCEEDED);
            }

            void OnTick()
            {
                if (Ask > Bid)
                {
                    Print("spread");
                }
            }
            """;

        var result = _sut.Validate(code, ExportFormat.MQL5);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    #endregion

    #region MQL — Known-Bad Patterns

    [Fact]
    public void Validate_MQL4_MissingOnInit_ReportsError()
    {
        const string code = """
            void OnTick()
            {
                double ma = iMA(NULL, 0, 14, 0, MODE_SMA, PRICE_CLOSE, 0);
            }
            """;

        var result = _sut.Validate(code, ExportFormat.MQL4);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Section == "OnInit");
    }

    [Fact]
    public void Validate_MQL4_MissingOnTick_ReportsError()
    {
        const string code = """
            int OnInit()
            {
                return(INIT_SUCCEEDED);
            }
            """;

        var result = _sut.Validate(code, ExportFormat.MQL4);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Section == "OnTick");
    }

    [Fact]
    public void Validate_MQL5_MissingBothHandlers_ReportsMultipleErrors()
    {
        const string code = """
            //+------------------------------------------------------------------+
            //| Script program start function                                      |
            //+------------------------------------------------------------------+
            void OnStart()
            {
                Print("This is a script, not an EA");
            }
            """;

        var result = _sut.Validate(code, ExportFormat.MQL5);

        Assert.False(result.IsValid);
        Assert.True(result.Errors.Count >= 2);
        Assert.Contains(result.Errors, e => e.Section == "OnInit");
        Assert.Contains(result.Errors, e => e.Section == "OnTick");
    }

    [Fact]
    public void Validate_MQL4_UnmatchedBraces_ReportsError()
    {
        const string code = """
            int OnInit()
            {
                return(INIT_SUCCEEDED);

            void OnTick()
            {
                Print("test");
            }
            """;

        var result = _sut.Validate(code, ExportFormat.MQL4);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Section == "braces");
    }

    [Fact]
    public void Validate_MQL5_ExtraClosingBrace_ReportsError()
    {
        const string code = """
            int OnInit()
            {
                return(INIT_SUCCEEDED);
            }
            }

            void OnTick()
            {
                Print("test");
            }
            """;

        var result = _sut.Validate(code, ExportFormat.MQL5);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Section == "braces");
    }

    #endregion

    #region Error Reporting Quality

    [Fact]
    public void Validate_ErrorsContainMeaningfulMessages()
    {
        const string code = """
            // Empty Pine Script
            plot(close)
            """;

        var result = _sut.Validate(code, ExportFormat.PineScript);

        Assert.False(result.IsValid);
        foreach (var error in result.Errors)
        {
            Assert.False(string.IsNullOrWhiteSpace(error.Message));
            Assert.False(string.IsNullOrWhiteSpace(error.Section));
        }
    }

    [Fact]
    public void Validate_PineScript_InvalidVersion_ReportsLineNumber()
    {
        const string code = "//@version=abc\nstrategy(\"Test\")";

        var result = _sut.Validate(code, ExportFormat.PineScript);

        Assert.False(result.IsValid);
        var versionError = result.Errors.First(e => e.Section == "version directive");
        Assert.Equal(1, versionError.Line);
    }

    [Fact]
    public void Validate_BracesInComments_AreIgnored()
    {
        const string code = """
            //@version=5
            strategy("Test", overlay=true)

            // This comment has { unmatched braces
            // And another one }}}
            longCondition = close > open
            if (longCondition)
                strategy.entry("Long", strategy.long)
            """;

        var result = _sut.Validate(code, ExportFormat.PineScript);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    #endregion
}
