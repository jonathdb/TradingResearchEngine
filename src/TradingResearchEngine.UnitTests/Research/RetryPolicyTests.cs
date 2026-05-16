using TradingResearchEngine.Application.Research;

namespace TradingResearchEngine.UnitTests.Research;

public class RetryPolicyTests
{
    private readonly RetryPolicy _sut = new();

    // ─── IsTransient classification ──────────────────────────────────────────

    [Fact]
    public void IsTransient_HttpRequestException_ReturnsTrue()
    {
        var ex = new HttpRequestException("Connection refused");
        Assert.True(_sut.IsTransient(ex));
    }

    [Fact]
    public void IsTransient_TimeoutException_ReturnsTrue()
    {
        var ex = new TimeoutException("Request timed out");
        Assert.True(_sut.IsTransient(ex));
    }

    [Fact]
    public void IsTransient_IOException_ReturnsTrue()
    {
        var ex = new IOException("Network stream closed");
        Assert.True(_sut.IsTransient(ex));
    }

    [Fact]
    public void IsTransient_InvalidOperationException_ReturnsFalse()
    {
        var ex = new InvalidOperationException("Invalid config");
        Assert.False(_sut.IsTransient(ex));
    }

    [Fact]
    public void IsTransient_ArgumentException_ReturnsFalse()
    {
        var ex = new ArgumentException("Bad parameter");
        Assert.False(_sut.IsTransient(ex));
    }

    [Fact]
    public void IsTransient_NullReferenceException_ReturnsFalse()
    {
        var ex = new NullReferenceException("Object reference not set");
        Assert.False(_sut.IsTransient(ex));
    }

    // ─── GetBackoffDelay ─────────────────────────────────────────────────────

    [Fact]
    public void GetBackoffDelay_FirstAttempt_ReturnsInitialBackoff()
    {
        var delay = _sut.GetBackoffDelay(0);
        Assert.Equal(TimeSpan.FromSeconds(2), delay);
    }

    [Fact]
    public void GetBackoffDelay_SecondAttempt_ReturnsDoubledBackoff()
    {
        var delay = _sut.GetBackoffDelay(1);
        Assert.Equal(TimeSpan.FromSeconds(4), delay);
    }

    [Fact]
    public void GetBackoffDelay_ThirdAttempt_ReturnsQuadrupledBackoff()
    {
        var delay = _sut.GetBackoffDelay(2);
        Assert.Equal(TimeSpan.FromSeconds(8), delay);
    }

    [Fact]
    public void GetBackoffDelay_CustomMultiplier_AppliesCorrectly()
    {
        var policy = new RetryPolicy
        {
            InitialBackoff = TimeSpan.FromSeconds(1),
            BackoffMultiplier = 3.0
        };

        Assert.Equal(TimeSpan.FromSeconds(1), policy.GetBackoffDelay(0));
        Assert.Equal(TimeSpan.FromSeconds(3), policy.GetBackoffDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(9), policy.GetBackoffDelay(2));
    }

    // ─── Default values ──────────────────────────────────────────────────────

    [Fact]
    public void Defaults_MaxRetries_IsThree()
    {
        Assert.Equal(3, _sut.MaxRetries);
    }

    [Fact]
    public void Defaults_InitialBackoff_IsTwoSeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), _sut.InitialBackoff);
    }

    [Fact]
    public void Defaults_BackoffMultiplier_IsTwo()
    {
        Assert.Equal(2.0, _sut.BackoffMultiplier);
    }
}
