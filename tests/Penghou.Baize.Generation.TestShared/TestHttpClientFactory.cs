namespace Penghou.Baize.Generation.TestShared;

/// <summary>Returns the same <see cref="HttpClient"/> for any client name.</summary>
public sealed class TestHttpClientFactory : IHttpClientFactory
{
    private readonly HttpClient _client;

    /// <summary>Wraps an existing client.</summary>
    /// <param name="client">The client to return for every name.</param>
    public TestHttpClientFactory(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    /// <inheritdoc />
    public HttpClient CreateClient(string name) => _client;
}