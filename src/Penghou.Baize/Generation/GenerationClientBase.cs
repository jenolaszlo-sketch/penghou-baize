using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Penghou.Baize.Generation;

/// <summary>
/// Shared transport mechanics for generation clients: named Http client
/// creation, submission-aware error classification, JSON reading, and operation
/// handle creation. Providers model the wire protocol themselves and never
/// annotate the common contracts with provider attributes.
/// </summary>
public abstract class GenerationClientBase : IGenerationClient
{
    /// <summary>JSON options used to read provider responses.</summary>
    protected static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _provider;
    private readonly string _endpointId;
    private readonly string _model;
    private readonly string _apiKey;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GenerationCapabilities _capabilities;

    /// <summary>Initializes the shared generation transport state.</summary>
    /// <param name="provider">The provider name used in operation handles.</param>
    /// <param name="endpointId">The configured endpoint identity.</param>
    /// <param name="model">The model identifier the endpoint is bound to.</param>
    /// <param name="httpClientFactory">The application HTTP client factory (named client <c>llm</c>).</param>
    /// <param name="apiKey">The resolved API key, or an empty string when none is required.</param>
    /// <param name="capabilities">The effective endpoint capabilities used for validation.</param>
    protected GenerationClientBase(
        string provider,
        string endpointId,
        string model,
        IHttpClientFactory httpClientFactory,
        string apiKey,
        GenerationCapabilities capabilities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointId);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(capabilities);
        _provider = provider;
        _endpointId = endpointId;
        _model = model;
        _httpClientFactory = httpClientFactory;
        _apiKey = apiKey;
        _capabilities = capabilities;
    }

    /// <inheritdoc />
    public GenerationCapabilities Capabilities => _capabilities;

    /// <inheritdoc />
    public abstract Task<GenerationOperation> SubmitAsync(
        GenerationRequest request,
        CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task<GenerationOperation> GetAsync(
        GenerationOperationHandle handle,
        CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task<GenerationOperation> CancelAsync(
        GenerationOperationHandle handle,
        CancellationToken cancellationToken = default);

    /// <summary>The provider name used in operation handles.</summary>
    protected string Provider => _provider;

    /// <summary>The configured endpoint identity.</summary>
    protected string EndpointId => _endpointId;

    /// <summary>The model identifier the endpoint is bound to.</summary>
    protected string Model => _model;

    /// <summary>The resolved API key, or an empty string when none is required.</summary>
    protected string ApiKey => _apiKey;

    /// <summary>A human-readable endpoint label used in validation messages.</summary>
    protected virtual string EndpointDescription => $"{_provider} endpoint '{_endpointId}'";

    /// <summary>
    /// The provider display name used in handle-ownership errors; defaults to
    /// <see cref="Provider"/>. Override when the wire display differs from the
    /// registry provider key (for example "OpenAI" vs "OpenAi").
    /// </summary>
    protected virtual string ProviderDisplayName => _provider;

    /// <summary>Creates an operation handle pinned to this endpoint.</summary>
    /// <param name="id">The provider-assigned operation id.</param>
    /// <returns>The pinned handle.</returns>
    protected GenerationOperationHandle CreateHandle(string id) =>
        new(_provider, _endpointId, id, _model);

    /// <summary>
    /// Rejects handles that were not issued by this endpoint, so status reads
    /// and cancellations can never be routed to the wrong provider or
    /// endpoint by a persisted handle.
    /// </summary>
    /// <param name="handle">The operation handle to check.</param>
    protected void EnsureHandleOwnership(GenerationOperationHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (!string.Equals(handle.Provider, _provider, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(handle.EndpointId, _endpointId, StringComparison.Ordinal))
        {
            throw BaizeException.InvalidRequest(
                $"Handle '{handle.Provider}/{handle.EndpointId}/{handle.Id}' does not belong to " +
                $"{ProviderDisplayName} endpoint '{_endpointId}'.");
        }
    }

    /// <summary>
    /// Validates the request against the endpoint capabilities. Providers that
    /// need additional model-specific validation override this to chain.
    /// </summary>
    /// <param name="request">The modality-specific request.</param>
    protected virtual void ValidateRequest(GenerationRequest request) =>
        GenerationRequestValidator.Validate(_capabilities, request, EndpointDescription);

    /// <summary>
    /// Sends a request and returns the response. Failures before acceptance are
    /// converted to <see cref="BaizeException"/>; non-success HTTP responses are
    /// classified via <see cref="ClassifyFailure"/>. When <paramref name="submission"/>
    /// is true a connection-level failure surfaces as
    /// <see cref="GenerationErrorKind.UnknownSubmissionOutcome"/> so an ambiguous
    /// submission is never retried automatically.
    /// </summary>
    /// <param name="httpRequest">The request to send.</param>
    /// <param name="context">A human-readable context label for error messages (for example <c>image submission</c>).</param>
    /// <param name="submission">Whether this request submits a billable operation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The success response.</returns>
    protected async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage httpRequest,
        string context,
        bool submission,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpRequest);

        var started = Stopwatch.GetTimestamp();
        var operationName = submission ? "submit" : context.Contains("cancel", StringComparison.OrdinalIgnoreCase) ? "cancel" : "status";
        using var activity = BaizeTelemetry.Activities.StartActivity("llm.generation", ActivityKind.Client);
        activity?.SetTag("gen_ai.operation.name", operationName);
        activity?.SetTag("gen_ai.provider.name", _provider);
        activity?.SetTag("gen_ai.request.model", _model);
        activity?.SetTag("baize.gen.endpoint", _endpointId);
        var tags = new TagList
        {
            { "gen_ai.operation.name", operationName },
            { "gen_ai.provider.name", _provider },
            { "gen_ai.request.model", _model }
        };
        BaizeTelemetry.GenerationRequests.Add(1, tags);

        HttpResponseMessage response;
        try
        {
            try
            {
                var httpClient = _httpClientFactory.CreateClient(BaizeHttp.ClientName);
                response = await httpClient.SendAsync(
                    httpRequest,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                throw submission
                    ? BaizeException.UnknownSubmissionOutcome(
                        $"{context} timed out; the provider may or may not have accepted the operation.",
                        ex)
                    : BaizeException.ProviderUnavailable($"{context} timed out before a response.", ex);
            }
            catch (HttpRequestException ex)
            {
                throw submission
                    ? BaizeException.UnknownSubmissionOutcome(
                        $"{context} failed before a response; the provider may or may not have accepted the operation.",
                        ex)
                    : BaizeException.ProviderUnavailable($"{context} failed before a response.", ex);
            }

            if (!response.IsSuccessStatusCode)
                await ThrowForNonSuccessAsync(response, context, cancellationToken);

            return response;
        }
        catch (BaizeException exception)
        {
            RecordGenerationFailure(activity, exception, submission, context);
            throw;
        }
        finally
        {
            BaizeTelemetry.GenerationDuration.Record(
                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                tags);
        }
    }

    /// <summary>
    /// Reads and parses the JSON body of a success response, converting malformed
    /// payloads into <see cref="GenerationErrorKind.GenerationFailed"/>.
    /// </summary>
    /// <param name="response">The success response.</param>
    /// <param name="context">A human-readable context label for error messages.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The parsed JSON root, cloned so it stays valid independently of the backing document.</returns>
    protected static async Task<JsonElement> ReadJsonAsync(
        HttpResponseMessage response,
        string context,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            // Clone detaches the element from the document's pooled buffers,
            // so no undisposed JsonDocument is leaked to callers.
            using var document = JsonDocument.Parse(body);
            return document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new BaizeException(
                $"{context} returned a malformed response.",
                GenerationErrorKind.GenerationFailed,
                (int)response.StatusCode,
                body,
                ex);
        }
    }

    /// <summary>
    /// Classifies a non-success HTTP response. Providers with a richer in-body
    /// vocabulary override this to refine the classification (for example Gemini
    /// canonical REST statuses or Runway error codes).
    /// </summary>
    /// <param name="response">The non-success response.</param>
    /// <param name="responseBody">The response body text.</param>
    /// <returns>The normalized failure classification.</returns>
    protected virtual GenerationErrorKind ClassifyFailure(
        HttpResponseMessage response,
        string responseBody) =>
        BaizeException.ClassifyStatusCode((int)response.StatusCode);

    /// <summary>
    /// Reads a success response body as UTF-8 text.
    /// </summary>
    /// <param name="response">The success response.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The response body bytes.</returns>
    protected static async Task<byte[]> ReadBytesAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken) =>
        await response.Content.ReadAsByteArrayAsync(cancellationToken);

    /// <summary>
    /// Builds a JSON content body.
    /// </summary>
    /// <param name="value">The value to serialize.</param>
    /// <returns>UTF-8 JSON content.</returns>
    protected static ByteArrayContent JsonContent(object value) =>
        new(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, JsonOptions)));

    /// <summary>
    /// Deserializes a success response root, converting empty or malformed
    /// payloads into <see cref="GenerationErrorKind.GenerationFailed"/>.
    /// </summary>
    /// <typeparam name="T">The wire response type.</typeparam>
    /// <param name="root">The parsed JSON root.</param>
    /// <param name="context">A human-readable context label for error messages.</param>
    /// <returns>The deserialized wire payload.</returns>
    protected static T Deserialize<T>(JsonElement root, string context)
    {
        try
        {
            return root.Deserialize<T>(JsonOptions) ??
                throw new BaizeException(
                    $"{context} returned an empty response.",
                    GenerationErrorKind.GenerationFailed);
        }
        catch (JsonException ex)
        {
            throw new BaizeException(
                $"{context} returned a malformed response.",
                GenerationErrorKind.GenerationFailed,
                innerException: ex);
        }
    }

    /// <summary>
    /// Applies the endpoint credential to a request. The default writes a Bearer
    /// authorization header; providers whose API uses a different credential
    /// (for example Gemini's <c>x-goog-api-key</c> header) override this.
    /// </summary>
    /// <param name="httpRequest">The request to authorize.</param>
    protected virtual void ApplyAuth(HttpRequestMessage httpRequest)
    {
        if (!string.IsNullOrEmpty(_apiKey))
            httpRequest.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
    }

    private async Task ThrowForNonSuccessAsync(
        HttpResponseMessage response,
        string context,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        throw new BaizeException(
            $"{context} failed with HTTP {(int)response.StatusCode}: {body}",
            ClassifyFailure(response, body),
            (int)response.StatusCode,
            body,
            null);
    }

    private void RecordGenerationFailure(Activity? activity, BaizeException exception, bool submission, string context)
    {
        activity?.SetStatus(ActivityStatusCode.Error);
        activity?.SetTag("error.type", exception.GetType().FullName);
        BaizeTelemetry.GenerationFailures.Add(1,
            new KeyValuePair<string, object?>[]
            {
                new("gen_ai.operation.name", submission ? "submit" : context.Contains("cancel", StringComparison.OrdinalIgnoreCase) ? "cancel" : "status"),
                new("gen_ai.provider.name", _provider),
                new("gen_ai.request.model", _model),
                new("error.type", exception.GetType().Name)
            });
    }
}