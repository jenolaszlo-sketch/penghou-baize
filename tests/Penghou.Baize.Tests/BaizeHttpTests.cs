using System.Net.Http;
using FluentAssertions;

namespace Penghou.Baize.Tests;

/// <summary>
/// Per-model request-timeout wrapping: every client handed out carries the
/// configured timeout, non-positive values are rejected eagerly, and callers
/// that never opt in keep whatever timeout the transport registered.
/// </summary>
public sealed class BaizeHttpTests
{
    private sealed class SingletonFactory(HttpClient client) : IHttpClientFactory
    {
        public int CreationCount { get; private set; }

        public HttpClient CreateClient(string name)
        {
            CreationCount++;
            return client;
        }
    }

    [Fact]
    public void WithRequestTimeout_StampsTimeoutOnCreatedClients()
    {
        var shared = new HttpClient { Timeout = TimeSpan.FromSeconds(100) };
        var inner = new SingletonFactory(shared);

        var wrapped = BaizeHttp.WithRequestTimeout(inner, TimeSpan.FromSeconds(42));
        var created = wrapped.CreateClient("llm");

        // The wrapper delegates creation and stamps whatever comes back —
        // with a real factory each returned instance is fresh, so stamping
        // never leaks across consumers.
        created.Timeout.Should().Be(TimeSpan.FromSeconds(42));
        inner.CreationCount.Should().Be(1);
    }

    [Fact]
    public void WithRequestTimeout_DelegatesEveryCreateToTheInnerFactory()
    {
        var inner = new SingletonFactory(new HttpClient());
        var wrapped = BaizeHttp.WithRequestTimeout(inner, TimeSpan.FromSeconds(5));

        wrapped.CreateClient("llm");
        wrapped.CreateClient("chat");

        inner.CreationCount.Should().Be(2);
    }

    [Fact]
    public void UnwrappedFactory_ClientsKeepTheirConfiguredTimeout()
    {
        var shared = new HttpClient { Timeout = TimeSpan.FromSeconds(100) };
        var inner = new SingletonFactory(shared);

        inner.CreateClient("llm").Timeout.Should().Be(TimeSpan.FromSeconds(100));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void WithRequestTimeout_RejectsNonPositiveTimeouts(int seconds)
    {
        var inner = new SingletonFactory(new HttpClient());

        var act = () => BaizeHttp.WithRequestTimeout(
            inner,
            TimeSpan.FromSeconds(seconds));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
