using System.Globalization;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Penghou.Baize;

/// <summary>
/// Base class for provider clients. Handles the shared HTTP streaming flow and
/// delegates provider-specific request shaping and event parsing to subclasses.
/// </summary>
/// <typeparam name="TWireRequest">The provider-specific wire request type.</typeparam>
public abstract class LlmClientBase<TWireRequest> : ILlmClient
{
    /// <summary>The provider model identifier used on the wire.</summary>
    protected readonly string Model;

    /// <summary>The HTTP client factory used to create the streaming client.</summary>
    protected readonly IHttpClientFactory HttpClientFactory;

    /// <summary>The API key used to authenticate, when any.</summary>
    protected readonly string ApiKey;

    /// <summary>The declared capabilities of the endpoint, queryable via <see cref="ILlmClient.Capabilities"/>.</summary>
    public LlmEndpointCapabilities Capabilities { get; }

    /// <summary>Initializes a provider client.</summary>
    /// <param name="model">The provider model identifier.</param>
    /// <param name="httpClientFactory">The HTTP client factory used to create the streaming client.</param>
    /// <param name="apiKey">The API key used to authenticate, when any.</param>
    /// <param name="capabilities">The declared capabilities of the endpoint.</param>
    protected LlmClientBase(
        string model,
        IHttpClientFactory httpClientFactory,
        string apiKey,
        LlmEndpointCapabilities capabilities)
    {
        Model = model;
        HttpClientFactory = httpClientFactory;
        ApiKey = apiKey;
        Capabilities = capabilities;
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

        ValidateRequest(request);

        var wireRequest = ToWireRequest(request);

        using var httpRequest = CreateHttpRequest(wireRequest);

        var httpClient = HttpClientFactory.CreateClient("llm");

        using var response = await httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            throw new LlmClientException(
                $"LLM streaming request failed with HTTP {(int)response.StatusCode}: {responseBody}",
                (int)response.StatusCode,
                ReadRateLimitInfo(response));
        }

        var rateLimit = ReadRateLimitInfo(response);

        await using var stream =
            await response.Content.ReadAsStreamAsync(cancellationToken);

        await foreach (var evt in ProcessStreamAsync(stream, cancellationToken))
            yield return evt;

        if (rateLimit is not null)
            yield return new LlmStreamEvent(RateLimit: rateLimit);
    }

    /// <summary>
    /// Validates <paramref name="request"/> against the endpoint's declared
    /// capabilities before transmission, throwing
    /// <see cref="LlmRequestValidationException"/> for any requested feature
    /// the endpoint does not support. Subclasses can override this to add
    /// provider-specific rules; call the base implementation first.
    /// </summary>
    /// <param name="request">The request to validate.</param>
    protected virtual void ValidateRequest(LlmRequest request)
    {
        if (request.Tools.Count > 0 && !Capabilities.NativeToolCalling)
        {
            throw new LlmRequestValidationException(
                $"Endpoint '{Model}' does not support native tool calling, " +
                $"but the request declares {request.Tools.Count} tool(s).");
        }

        var toolCallParts = request.Messages
            .SelectMany(message => message.Parts)
            .OfType<LlmToolCallContent>()
            .ToList();
        var toolResultParts = request.Messages
            .SelectMany(message => message.Parts)
            .OfType<LlmToolResultContent>()
            .ToList();

        if ((toolCallParts.Count > 0 || toolResultParts.Count > 0) &&
            !Capabilities.NativeToolCalling)
        {
            throw new LlmRequestValidationException(
                $"Endpoint '{Model}' does not support native tool calling, " +
                "but the request replays assistant tool calls and/or tool results.");
        }

        if (!Capabilities.ParallelToolCalls)
        {
            var messageWithParallelCalls = request.Messages
                .Select(message => message.Parts
                    .OfType<LlmToolCallContent>()
                    .ToList())
                .FirstOrDefault(parts => parts.Count > 1);

            if (messageWithParallelCalls is not null)
            {
                throw new LlmRequestValidationException(
                    $"Endpoint '{Model}' does not support parallel tool calls, " +
                    $"but an assistant message replays {messageWithParallelCalls.Count} tool calls.");
            }
        }

        if (request.ResponseFormat is not null &&
            !Capabilities.NativeStructuredOutput &&
            !Capabilities.StructuredOutputViaTool)
        {
            throw new LlmRequestValidationException(
                $"Endpoint '{Model}' does not support structured output, " +
                "but the request specifies a response format.");
        }

        if (request.ThinkingConfig is { Mode: LlmThinkingMode.Enabled } &&
            !Capabilities.Thinking)
        {
            throw new LlmRequestValidationException(
                $"Endpoint '{Model}' does not support extended thinking, " +
                "but the request enables it.");
        }

        if (request.ThinkingConfig is { Mode: LlmThinkingMode.Disabled } &&
            !Capabilities.ThinkingDisable)
        {
            throw new LlmRequestValidationException(
                $"Endpoint '{Model}' does not support disabling extended " +
                "thinking, but the request disables it.");
        }

        if (request.ThinkingConfig is
            {
                Mode: LlmThinkingMode.Enabled,
                Effort: not LlmThinkingEffort.None
            } thinking &&
            !Capabilities.SupportedThinkingEfforts.Contains(thinking.Effort))
        {
            throw new LlmRequestValidationException(
                $"Endpoint '{Model}' does not support thinking effort " +
                $"'{thinking.Effort}', but the request requests it.");
        }

        foreach (var part in request.Messages
            .SelectMany(message => message.Parts))
        {
            var contentType = ContentTypeOf(part);

            if (contentType is { } type &&
                !Capabilities.ContentTypes.Contains(type))
            {
                throw new LlmRequestValidationException(
                    $"Endpoint '{Model}' does not support content type " +
                    $"'{type}', but the request includes it.");
            }
        }
    }

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
            _ => null
        };

    /// <summary>Converts a canonical request into the provider wire request.</summary>
    /// <param name="request">The canonical request.</param>
    /// <returns>The provider wire request.</returns>
    protected abstract TWireRequest ToWireRequest(LlmRequest request);

    /// <summary>Creates the HTTP request message for a provider wire request.</summary>
    /// <param name="wireRequest">The provider wire request.</param>
    /// <returns>The HTTP request message to send.</returns>
    protected abstract HttpRequestMessage CreateHttpRequest(TWireRequest wireRequest);

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
    /// Accepts <c>data:</c> with or without a trailing space and terminates on
    /// the "[DONE]" sentinel.
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

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);

            if (line is null)
                break;

            if (line.Length == 0)
            {
                if (dataLines is { Count: > 0 })
                {
                    var payload = string.Join('\n', dataLines);
                    dataLines = null;

                    if (payload == "[DONE]")
                        yield break;

                    if (!string.IsNullOrWhiteSpace(payload))
                        yield return (eventType, payload);
                }

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

            if (payload != "[DONE]" && !string.IsNullOrWhiteSpace(payload))
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
        string context)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new LlmClientException($"Missing JSON for {context}.");

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new LlmClientException($"Failed to parse {context}: {json}", ex);
        }
    }

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
