using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Penghou.Baize;

/// <summary>
/// Shared transport mechanics for native batch clients: named-client
/// acquisition, send-and-classify with provider display naming, credential
/// helpers, and JSONL splitting. Providers implement the wire protocol
/// (submit/status/results/cancel payloads and mappers) on top.
/// </summary>
public abstract class BaizeBatchClientBase : IBaizeBatchClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _apiKey;
    private readonly BatchCapabilities _capabilities;

    /// <summary>Initializes the shared batch transport state.</summary>
    /// <param name="providerId">The registry provider key exposed via <see cref="IBaizeBatchClient.ProviderId"/>.</param>
    /// <param name="model">The model identifier the endpoint is bound to.</param>
    /// <param name="httpClientFactory">The application HTTP client factory.</param>
    /// <param name="apiKey">The resolved API key, or an empty string when none is required.</param>
    /// <param name="capabilities">The effective endpoint capabilities.</param>
    protected BaizeBatchClientBase(
        string providerId,
        string model,
        IHttpClientFactory httpClientFactory,
        string apiKey,
        LlmEndpointCapabilities capabilities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(capabilities);
        _capabilities = capabilities.Batch;
        _httpClientFactory = httpClientFactory;
        _apiKey = apiKey;
        ProviderId = providerId;
        Model = model;
    }

    /// <inheritdoc />
    public string ProviderId { get; }

    /// <inheritdoc />
    public BatchCapabilities Capabilities => _capabilities;

    /// <summary>Submits a logical batch to the provider.</summary>
    public abstract Task<ProviderBatchHandle> SubmitAsync(
        IReadOnlyList<BaizeBatchItem> items,
        BatchSubmissionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the provider's batch status.</summary>
    public abstract Task<ProviderBatchStatus> GetStatusAsync(
        ProviderBatchHandle handle,
        CancellationToken cancellationToken = default);

    /// <summary>Downloads and normalizes the provider's batch results.</summary>
    public abstract Task<IReadOnlyList<BaizeBatchResult>> GetResultsAsync(
        ProviderBatchHandle handle,
        CancellationToken cancellationToken = default);

    /// <summary>Cancels the provider's batch when supported.</summary>
    public abstract Task CancelAsync(
        ProviderBatchHandle handle,
        CancellationToken cancellationToken = default);

    /// <summary>The model identifier this batch endpoint is bound to.</summary>
    protected string Model { get; }

    /// <summary>The resolved API key, or an empty string when none is required.</summary>
    protected string ApiKey => _apiKey;

    /// <summary>
    /// Display name used in failure messages; defaults to
    /// <see cref="ProviderId"/>. Override when wire display differs from the
    /// registry key (for example "OpenAI" vs "OpenAi").
    /// </summary>
    protected virtual string ProviderDisplayName => ProviderId;

    /// <summary>
    /// Applies the provider credential scheme. Called by concrete clients
    /// before every send; implementations must be idempotent-safe about empty
    /// keys (omit rather than send blanks).
    /// </summary>
    protected abstract void ApplyAuth(HttpRequestMessage request);

    /// <summary>Creates the shared named transport client.</summary>
    protected HttpClient CreateTransport() =>
        _httpClientFactory.CreateClient(BaizeHttp.ClientName);

    /// <summary>
    /// Sends an authenticated batch request and deserializes the success body,
    /// converting HTTP failures and malformed payloads into
    /// <see cref="LlmClientException"/> with the provider's display name.
    /// </summary>
    /// <typeparam name="T">The wire response type.</typeparam>
    /// <param name="request">The pre-authorized request.</param>
    /// <param name="serializerOptions">The provider's wire serializer options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected async Task<T> SendAsync<T>(
        HttpRequestMessage request,
        JsonSerializerOptions serializerOptions,
        CancellationToken cancellationToken)
    {
        using var response = await CreateTransport()
            .SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);

        var responseBody = await response.Content.ReadAsStringAsync(
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new LlmClientException(
                $"{ProviderDisplayName} batch request failed with HTTP {(int)response.StatusCode}: " +
                LlmJson.FormatForError(responseBody),
                (int)response.StatusCode);
        }

        try
        {
            return JsonSerializer.Deserialize<T>(responseBody, serializerOptions)
                ?? throw new LlmClientException(
                    $"{ProviderDisplayName} returned an empty {typeof(T).Name} body.",
                    LlmClientFailureKind.Protocol);
        }
        catch (JsonException ex)
        {
            throw new LlmClientException(
                $"Failed to parse {ProviderDisplayName} batch response: " +
                LlmJson.FormatForError(responseBody),
                ex);
        }
    }

    /// <summary>Writes a Bearer authorization header when a key is configured.</summary>
    protected void ApplyBearerAuth(HttpRequestMessage request)
    {
        if (!string.IsNullOrEmpty(_apiKey))
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
    }

    /// <summary>Adds a named credential header when a key is configured.</summary>
    protected void ApplyCredentialHeader(
        HttpRequestMessage request,
        string headerName)
    {
        if (!string.IsNullOrEmpty(_apiKey))
            request.Headers.Add(headerName, _apiKey);
    }

    /// <summary>Splits a JSONL document into non-blank lines.</summary>
    protected static IEnumerable<string> SplitJsonl(string content)
    {
        using var reader = new StringReader(content);

        while (reader.ReadLine() is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
                yield return line;
        }
    }
}
