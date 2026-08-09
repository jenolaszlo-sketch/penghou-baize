using FluentAssertions;

namespace Penghou.Baize.Tests;

public sealed class LlmRateLimitInfoTests
{
    [Fact]
    public void UnavailableUntil_UsesLatestResetAndRetryAfter()
    {
        var now = DateTimeOffset.UtcNow;
        var info = new LlmRateLimitInfo(
            RequestsResetAt: now.AddSeconds(10),
            TokensResetAt: now.AddSeconds(30),
            RetryAfter: TimeSpan.FromSeconds(5));

        info.UnavailableUntil.Should()
            .BeCloseTo(now.AddSeconds(30), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void UnavailableUntil_WithoutSignals_IsNull()
    {
        var info = new LlmRateLimitInfo(
            RequestsRemaining: 5,
            RequestsLimit: 10);

        info.UnavailableUntil.Should().BeNull();
    }

    [Fact]
    public void UnavailableUntil_PrefersRetryAfterWhenNoResets()
    {
        var info = new LlmRateLimitInfo(
            RetryAfter: TimeSpan.FromSeconds(7));

        info.UnavailableUntil.Should()
            .BeCloseTo(
                DateTimeOffset.UtcNow.AddSeconds(7),
                TimeSpan.FromSeconds(2));
    }
}

public sealed class LlmClientExceptionRateLimitTests
{
    [Fact]
    public void RateLimit_IsCarriedOnException()
    {
        var rateLimit = new LlmRateLimitInfo(TokensRemaining: 0);
        var exception = new LlmClientException(
            "rate limited",
            statusCode: 429,
            rateLimit);

        exception.StatusCode.Should().Be(429);
        exception.RateLimit.Should().Be(rateLimit);
        exception.InnerException.Should().BeNull();
    }
}
