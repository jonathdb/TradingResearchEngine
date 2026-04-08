using TradingResearchEngine.Application.Sessions;

namespace TradingResearchEngine.UnitTests.Sessions;

public class SessionCalendarTests
{
    // --- Forex ---

    [Fact]
    public void Forex_Weekday_IsTradable()
    {
        var cal = new ForexSessionCalendar();
        var monday = new DateTimeOffset(2024, 6, 3, 10, 0, 0, TimeSpan.Zero); // Monday 10:00 UTC
        Assert.True(cal.IsTradable(monday));
    }

    [Fact]
    public void Forex_Saturday_NotTradable()
    {
        var cal = new ForexSessionCalendar();
        var saturday = new DateTimeOffset(2024, 6, 1, 10, 0, 0, TimeSpan.Zero);
        Assert.False(cal.IsTradable(saturday));
    }

    [Fact]
    public void Forex_Sunday_NotTradable()
    {
        var cal = new ForexSessionCalendar();
        var sunday = new DateTimeOffset(2024, 6, 2, 10, 0, 0, TimeSpan.Zero);
        Assert.False(cal.IsTradable(sunday));
    }

    [Fact]
    public void Forex_AsiaSession_Classified()
    {
        var cal = new ForexSessionCalendar();
        var asia = new DateTimeOffset(2024, 6, 3, 3, 0, 0, TimeSpan.Zero); // 03:00 UTC Monday
        Assert.Equal("Asia", cal.ClassifySession(asia));
    }

    [Fact]
    public void Forex_LondonSession_Classified()
    {
        var cal = new ForexSessionCalendar();
        var london = new DateTimeOffset(2024, 6, 3, 8, 0, 0, TimeSpan.Zero); // 08:00 UTC
        Assert.Equal("London", cal.ClassifySession(london));
    }

    [Fact]
    public void Forex_OverlapSession_Classified()
    {
        var cal = new ForexSessionCalendar();
        var overlap = new DateTimeOffset(2024, 6, 3, 14, 0, 0, TimeSpan.Zero); // 14:00 UTC
        Assert.Equal("Overlap", cal.ClassifySession(overlap));
    }

    [Fact]
    public void Forex_NewYorkSession_Classified()
    {
        var cal = new ForexSessionCalendar();
        var ny = new DateTimeOffset(2024, 6, 3, 18, 0, 0, TimeSpan.Zero); // 18:00 UTC
        Assert.Equal("NewYork", cal.ClassifySession(ny));
    }

    [Fact]
    public void Forex_Weekend_ClassifiedAsClosed()
    {
        var cal = new ForexSessionCalendar();
        var sat = new DateTimeOffset(2024, 6, 1, 10, 0, 0, TimeSpan.Zero);
        Assert.Equal("Closed", cal.ClassifySession(sat));
    }

    // --- US Equity ---

    [Fact]
    public void UsEquity_RegularHours_IsTradable()
    {
        var cal = new UsEquitySessionCalendar();
        // Monday 10:00 ET = 14:00 UTC (EDT)
        var regular = new DateTimeOffset(2024, 6, 3, 14, 0, 0, TimeSpan.Zero);
        Assert.True(cal.IsTradable(regular));
    }

    [Fact]
    public void UsEquity_Weekend_NotTradable()
    {
        var cal = new UsEquitySessionCalendar();
        var sat = new DateTimeOffset(2024, 6, 1, 14, 0, 0, TimeSpan.Zero);
        Assert.False(cal.IsTradable(sat));
    }

    [Fact]
    public void UsEquity_RegularSession_Classified()
    {
        var cal = new UsEquitySessionCalendar();
        // Monday 10:00 ET = 14:00 UTC (EDT)
        var regular = new DateTimeOffset(2024, 6, 3, 14, 0, 0, TimeSpan.Zero);
        Assert.Equal("Regular", cal.ClassifySession(regular));
    }

    [Fact]
    public void UsEquity_PreMarket_Classified()
    {
        var cal = new UsEquitySessionCalendar();
        // Monday 05:00 ET = 09:00 UTC (EDT)
        var premarket = new DateTimeOffset(2024, 6, 3, 9, 0, 0, TimeSpan.Zero);
        Assert.Equal("PreMarket", cal.ClassifySession(premarket));
    }

    [Fact]
    public void UsEquity_AfterHours_Classified()
    {
        var cal = new UsEquitySessionCalendar();
        // Monday 17:00 ET = 21:00 UTC (EDT)
        var afterhours = new DateTimeOffset(2024, 6, 3, 21, 0, 0, TimeSpan.Zero);
        Assert.Equal("AfterHours", cal.ClassifySession(afterhours));
    }
}
