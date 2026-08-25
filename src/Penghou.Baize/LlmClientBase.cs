using System.Globalization;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Penghou.Baize;

/// <summary>
/// Base class for provider clients. Handles the shared HTTP streaming flow and
/// delegates provider-specific request shaping and event parsing to subclasses.
/// </summary>
public abstract class LlmClientBase : ILlmClient, ILlmClientMetadataProvider
{
    /// <summary>The provider model identifier used on the wire.</summary>
    protected string Model { get; }

    /// <summary>The HTTP client factory used to create the streaming client.</summary>
    protected IHttpClientFactory HttpClientFactory { get; }

    /// <summary>The API key used to authenticate, when any.</summary>
    protected string ApiKey { get; }

    /// <summary>The declared capabilities of the endpoint, queryable via <see cref="ILlmClient.Capabilities"/>.</summary>
    public LlmEndpointCapabilities Capabilities { get; }

    /// <inheritdoc />
    public LlmClientMetadata Metadata { get; }

    /// <summary>Initializes a provider client.</summary>
    /// <param name="model">The provider model identifier.</param>
    /// <param name="httpClientFactory">The HTTP client factory used to create the streaming client.</param>
    /// <param name="apiKey">The API key used to authenticate, when any.</param>
    /// <param name="capabilities">The declared capabilities of the endpoint.</param>
    /// <param name="provider">The provider name exposed through client metadata.</param>
    protected LlmClientBase(
        string model,
        IHttpClientFactory httpClientFactory,
        string apiKey,
        LlmEndpointCapabilities capabilities,
        string provider = "Unknown")
    {
        Model = model;
        HttpClientFactory = httpClientFactory;
        ApiKey = apiKey;
        Capabilities = capabilities;
        Metadata = new LlmClientMetadata(provider, model);
    }

    /// <summary>
    /// Streams the completion for <paramref name="request"/> by mapping it to a
    /// provider request and forwarding the parsed provider events.
    /// </summary>
    /// <param name="request">The request to send.</param>
    /// <param name="cancellationToken">Propagates notification that streaming should be cancelled.</param>
    public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        LlmRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var started = Stopwatch.GetTimestamp();
        using var activity = BaizeTelemetry.Activities.StartActivity(
            "llm.stream",
            ActivityKind.Client);
        activity?.SetTag("gen_ai.operation.name", "chat");
        activity?.SetTag("gen_ai.provider.name", Metadata.Provider);
        activity?.SetTag("gen_ai.request.model", Model);
        activity?.SetTag("gen_ai.request.tool_count", request.Tools.Count);
        var telemetryTags = new TagList
        {
            { "gen_ai.operation.name", "chat" },
            { "gen_ai.provider.name", Metadata.Provider },
            { "gen_ai.request.model", Model }
        };
        BaizeTelemetry.Requests.Add(1, telemetryTags);

        try
        {
            try
            {
                ValidateRequest(request);
            }
            catch (Exception exception)
            {
                RecordFailure(activity, exception, cancellationToken);
                throw;
            }

            HttpRequestMessage httpRequest;
            try
            {
                httpRequest = CreateHttpRequest(request);
                ApplyAuth(httpRequest);
            }
            catch (Exception exception)
            {
                RecordFailure(activity, exception, cancellationToken);
                throw;
            }
            using var httpRequestScope = httpRequest;

            var httpClient = HttpClientFactory.CreateClient(BaizeHttp.ClientName);

            HttpResponseMessage response;
            try
            {
                response = await httpClient.SendAsync(
                    httpRequest,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
            }
            catch (Exception exception)
            {
                RecordFailure(activity, exception, cancellationToken);
                throw;
            }
            using var responseScope = response;

            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                var exception = new LlmClientException(
                    $"LLM streaming request failed with HTTP {(int)response.StatusCode}: " +
                    LlmJson.FormatForError(responseBody),
                    (int)response.StatusCode,
                    ReadRateLimitInfo(response));
                RecordFailure(activity, exception, cancellationToken);
                throw exception;
            }

            var rateLimit = ReadRateLimitInfo(response);

            await using var stream =
                await response.Content.ReadAsStreamAsync(cancellationToken);

            await using var events = ProcessStreamAsync(stream, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
            while (true)
            {
                LlmStreamEvent evt;
                try
                {
                    if (!await events.MoveNextAsync())
                        break;
                    evt = events.Current;
                }
                catch (Exception exception)
                {
                    RecordFailure(activity, exception, cancellationToken);
                    throw;
                }

                if (evt.Usage is { } usage)
                {
                    if (usage.PromptTokens is { } input)
                        BaizeTelemetry.InputTokens.Add(input, telemetryTags);
                    if (usage.CompletionTokens is { } output)
                        BaizeTelemetry.OutputTokens.Add(output, telemetryTags);
                }

                yield return evt;
            }

            if (rateLimit is not null)
                yield return new LlmStreamEvent(RateLimit: rateLimit);

            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        finally
        {
            BaizeTelemetry.Duration.Record(
                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                telemetryTags);
        }
    }

    private void RecordFailure(
        Activity? activity,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // Caller-initiated cancellation is not a provider failure: counting
        // it inflates failure rates and poisons routing/cooldown signals.
        if (exception is OperationCanceledException &&
            cancellationToken.IsCancellationRequested)
        {
            return;
        }

        activity?.SetStatus(ActivityStatusCode.Error);
        activity?.SetTag("error.type", exception.GetType().FullName);
        BaizeTelemetry.Failures.Add(
            1,
            new KeyValuePair<string, object?>("gen_ai.operation.name", "chat"),
            new KeyValuePair<string, object?>("gen_ai.provider.name", Metadata.Provider),
            new KeyValuePair<string, object?>("gen_ai.request.model", Model),
            new KeyValuePair<string, object?>("error.type", exception.GetType().Name));
    }

    /// <summary>
    /// Validates <paramref name="request"/> against the endpoint's declared
    /// capabilities before transmission, throwing
    /// <see cref="LlmRequestValidationException"/> for any requested feature
    /// the endpoint does not support. Subclasses can override this to add
    /// provider-specific rules; call the base implementation first.
    /// </summary>
    /// <param name="request">The request to validate.</param>
    protected virtual void ValidateRequest(LlmRequest request) =>
        LlmRequestValidator.Validate(Model, Capabilities, request, ContentTypeOf);

    /// <summary>
    /// Maps a content part to the <see cref="LlmContentType"/> it carries,
    /// returning null for parts with no externally visible content type
    /// (for example tool calls and tool results).
    /// </summary>
    /// <param name="part">The content part to classify.</param>
    /// <returns>The content type of the part, or null when it has none.</returns>
    protected virtual LlmContentType? ContentTypeOf(LlmContentPart part) =>
        part switch
        {
            LlmTextContent => LlmContentType.Text,
            LlmReasoningContent => LlmContentType.Text,
            LlmImageContent => LlmContentType.Image,
            LlmAudioContent => LlmContentType.Audio,
            LlmVideoContent => LlmContentType.Video,
            LlmFileContent => LlmContentType.File,
            _ => null
        };

    /// <summary>Maps a canonical request and creates its provider HTTP request.</summary>
    /// <param name="request">The canonical request.</param>
    /// <returns>The HTTP request message to send.</returns>
    protected abstract HttpRequestMessage CreateHttpRequest(LlmRequest request);

    /// <summary>
    /// Applies the endpoint credential to an outgoing chat request. The
    /// template method invokes this right after <see cref="CreateHttpRequest"/>;
    /// the default writes a Bearer header when a key is configured. Override
    /// for provider-specific schemes (header names, query parameters).
    /// </summary>
    /// <param name="httpRequest">The request to authorize.</param>
    protected virtual void ApplyAuth(HttpRequestMessage httpRequest)
    {
        if (!string.IsNullOrEmpty(ApiKey))
            httpRequest.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiKey);
    }

    /// <summary>Parses the provider response stream into canonical events.</summary>
    /// <param name="stream">The response body stream.</param>
    /// <param name="cancellationToken">Propagates notification that streaming should be cancelled.</param>
    /// <returns>The canonical stream events.</returns>
    protected abstract IAsyncEnumerable<LlmStreamEvent> ProcessStreamAsync(
        Stream stream,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads an SSE stream line by line, buffering the <c>data:</c> fields of
    /// each event until the blank line that terminates it, then yielding the
    /// combined payload together with the most recently observed event type.
    /// Accepts <c>data:</c> with or without a trailing space. Provider-specific
    /// stream termini (such as OpenAI's <c>[DONE]</c>) are surfaced to callers
    /// rather than intercepted here, so each provider can validate that its own
    /// terminal signal arrived and reject truncated streams.
    /// </summary>
    /// <param name="stream">The response body stream.</param>
    /// <param name="cancellationToken">Propagates notification that streaming should be cancelled.</param>
    /// <returns>Pairs of the most recent event type and each data payload.</returns>
    protected static async IAsyncEnumerable<(string? EventType, string Data)> ReadSseEventsAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream);
        string? eventType = null;
        List<string>? dataLines = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);

            if (line is null)
                break;

            if (line.Length == 0)
            {
                if (dataLines is { Count: > 0 })
                {
                    var payload = string.Join('\n', dataLines);
                    dataLines = null;

                    if (!string.IsNullOrWhiteSpace(payload))
                        yield return (eventType, payload);
                }

                // The event-type buffer is cleared at each event boundary, so a follow-on
                // event without an event: field does not inherit the previous
                // type (per the WHATWG SSE "event type buffer" reset).
                eventType = null;

                continue;
            }

            if (line.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
            {
                eventType = line["event:".Length..].Trim();
                continue;
            }

            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                continue;

            dataLines ??= [];
            dataLines.Add(line["data:".Length..].TrimStart());
        }

        if (dataLines is { Count: > 0 })
        {
            var payload = string.Join('\n', dataLines);

            if (!string.IsNullOrWhiteSpace(payload))
                yield return (eventType, payload);
        }
    }

    /// <summary>
    /// Reads provider rate-limit and quota information from the response
    /// headers and the <c>Retry-After</c> header, returning null when none is
    /// reported. Handles both the OpenAI (<c>x-ratelimit-*</c>) and Anthropic
    /// (<c>anthropic-ratelimit-*</c>) header conventions.
    /// </summary>
    /// <param name="response">The HTTP response to inspect.</param>
    /// <returns>Rate-limit information, or null when the response reports none.</returns>
    protected static LlmRateLimitInfo? ReadRateLimitInfo(
        HttpResponseMessage response)
    {
        int? requestsRemaining =
            ReadIntHeader(response, "x-ratelimit-remaining-requests") ??
            ReadIntHeader(response, "anthropic-ratelimit-requests-remaining");
        int? requestsLimit =
            ReadIntHeader(response, "x-ratelimit-limit-requests") ??
            ReadIntHeader(response, "anthropic-ratelimit-requests-limit");
        DateTimeOffset? requestsReset =
            ReadCountdownReset(response, "x-ratelimit-reset-requests") ??
            ReadTimestampReset(response, "anthropic-ratelimit-requests-reset");
        int? tokensRemaining =
            ReadIntHeader(response, "x-ratelimit-remaining-tokens") ??
            ReadIntHeader(response, "anthropic-ratelimit-tokens-remaining");
        int? tokensLimit =
            ReadIntHeader(response, "x-ratelimit-limit-tokens") ??
            ReadIntHeader(response, "anthropic-ratelimit-tokens-limit");
        DateTimeOffset? tokensReset =
            ReadCountdownReset(response, "x-ratelimit-reset-tokens") ??
            ReadTimestampReset(response, "anthropic-ratelimit-tokens-reset");
        TimeSpan? retryAfter =
            response.Headers.RetryAfter?.Delta ??
            (response.Headers.RetryAfter?.Date is { } date
                ? date - DateTimeOffset.UtcNow
                : null);

        if (requestsRemaining is null && requestsLimit is null &&
            requestsReset is null && tokensRemaining is null &&
            tokensLimit is null && tokensReset is null && retryAfter is null)
        {
            return null;
        }

        return new LlmRateLimitInfo(
            RequestsRemaining: requestsRemaining,
            RequestsLimit: requestsLimit,
            RequestsResetAt: requestsReset,
            TokensRemaining: tokensRemaining,
            TokensLimit: tokensLimit,
            TokensResetAt: tokensReset,
            RetryAfter: retryAfter);
    }

    /// <summary>
    /// Parses <paramref name="json"/> into an owned <see cref="JsonElement"/>,
    /// throwing a <see cref="LlmClientException"/> with the given
    /// <paramref name="context"/> on malformed input.
    /// </summary>
    /// <param name="json">The JSON text to parse.</param>
    /// <param name="context">A description of where the JSON came from, used in error messages.</param>
    /// <returns>An owned clone of the parsed root element.</returns>
    protected static JsonElement ParseJsonElement(
        string? json,
        string context) =>
        LlmJson.ParseElement(json, context);

    private static int? ReadIntHeader(
        HttpResponseMessage response,
        string name)
    {
        if (!response.Headers.TryGetValues(name, out var values))
            return null;

        var raw = values.FirstOrDefault();

        return int.TryParse(
            raw,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;
    }

    private static DateTimeOffset? ReadCountdownReset(
        HttpResponseMessage response,
        string name)
    {
        if (!response.Headers.TryGetValues(name, out var values))
            return null;

        var raw = values.FirstOrDefault();

        if (raw is null)
            return null;

        var secondsText =
            raw.EndsWith("s", StringComparison.OrdinalIgnoreCase)
                ? raw[..^1]
                : raw;

        return double.TryParse(
            secondsText,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var seconds)
            ? DateTimeOffset.UtcNow.AddSeconds(seconds)
            : null;
    }

    private static DateTimeOffset? ReadTimestampReset(
        HttpResponseMessage response,
        string name)
    {
        if (!response.Headers.TryGetValues(name, out var values))
            return null;

        var raw = values.FirstOrDefault();

        return DateTimeOffset.TryParse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var value)
            ? value
            : null;
    }
}
