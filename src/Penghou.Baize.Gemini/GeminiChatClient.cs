using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Penghou.Baize;

namespace Penghou.Baize.Gemini;

/// <summary>
/// <see cref="ILlmClient"/> implementation for the Gemini streaming API
/// (<c>POST /v1beta/models/{model}:streamGenerateContent?alt=sse</c>). Streams
/// text and function-call deltas to the canonical event stream, surfaces
/// <c>thought</c> parts as reasoning, and maps finish reasons and usage into
/// the provider-neutral shape.
/// </summary>
public sealed class GeminiChatClient : LlmClientBase<GeminiChatRequest>
{
    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition =
                JsonIgnoreCondition.WhenWritingNull
        };

    private readonly Uri _chatUri;

    /// <summary>
    /// Creates a Gemini streaming client.
    /// </summary>
    /// <param name="model">The Gemini model identifier (for example <c>gemini-2.5-flash</c>).</param>
    /// <param name="httpClientFactory">Factory providing the underlying <see cref="HttpClient"/>.</param>
    /// <param name="apiKey">The Gemini API key.</param>
    /// <param name="baseUrl">Base API URL. When it does not already include a version segment such as <c>v1beta</c> or <c>v1</c>, <c>v1beta</c> is appended.</param>
    /// <param name="capabilities">The declared capabilities of the endpoint.</param>
    public GeminiChatClient(
        string model,
        IHttpClientFactory httpClientFactory,
        string apiKey,
        string baseUrl,
        LlmEndpointCapabilities capabilities)
        : base(model, httpClientFactory, apiKey, capabilities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);

        var normalizedBaseUrl =
            baseUrl.TrimEnd('/');
        var lastSegment =
            normalizedBaseUrl[
                (normalizedBaseUrl.LastIndexOf('/') + 1)..];
        var includeVersionSegment =
            !LooksLikeApiVersion(lastSegment);

        _chatUri = new Uri(
            $"{normalizedBaseUrl}" +
            $"{(includeVersionSegment ? "/v1beta" : string.Empty)}" +
            $"/models/{model}:streamGenerateContent?alt=sse");
    }

    /// <inheritdoc />
    protected override HttpRequestMessage CreateHttpRequest(GeminiChatRequest wireRequest)
    {
        var json = JsonSerializer.Serialize(
            wireRequest,
            JsonOptions);

        var httpRequest =
            new HttpRequestMessage(
                HttpMethod.Post,
                _chatUri);

        httpRequest.Headers.Add(
            "x-goog-api-key",
            ApiKey);

        httpRequest.Content =
            new StringContent(
                json,
                Encoding.UTF8,
                "application/json");

        return httpRequest;
    }

    /// <inheritdoc />
    protected override GeminiChatRequest ToWireRequest(LlmRequest request)
    {
        var contents =
            new List<GeminiChatMessage>();
        var systemText =
            new List<string>();

        foreach (var message in request.Messages)
        {
            var isSystem =
                string.Equals(
                    message.Role,
                    "system",
                    StringComparison.OrdinalIgnoreCase);

            if (isSystem)
            {
                foreach (var part in message.Parts)
                {
                    if (part is not LlmTextContent text)
                    {
                        throw new LlmRequestValidationException(
                            "Gemini accepts only text in the system " +
                            "instruction; a system message carries a " +
                            "non-text content part.");
                    }

                    systemText.Add(text.Text);
                }

                continue;
            }

            var role =
                string.Equals(message.Role, "tool", StringComparison.OrdinalIgnoreCase)
                    ? "user"
                    : string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                        ? "model"
                        : message.Role;
            var parts =
                new List<GeminiContentPart>();

            foreach (var part in message.Parts)
            {
                switch (part)
                {
                    case LlmTextContent text:
                        parts.Add(
                            new GeminiContentPart
                            {
                                Text = text.Text,
                                ThoughtSignature =
                                    text.Continuation?.GetValue(
                                        "thoughtSignature")
                            });
                        break;

                    case LlmReasoningContent reasoning:
                        parts.Add(
                            new GeminiContentPart
                            {
                                Text = reasoning.Text,
                                Thought = true,
                                ThoughtSignature =
                                    reasoning.Continuation?.GetValue(
                                        "thoughtSignature")
                            });
                        break;

                    case LlmToolCallContent toolCall:
                        parts.Add(
                            new GeminiContentPart
                            {
                                ThoughtSignature =
                                    (toolCall.ToolCall.Continuation ??
                                     toolCall.Continuation)?.GetValue(
                                        "thoughtSignature"),
                                FunctionCall =
                                    new GeminiFunctionCall
                                    {
                                        Id = toolCall.ToolCall.Id,
                                        Name = toolCall.ToolCall.Name,
                                        Args = ParseJsonElement(
                                            toolCall.ToolCall.ArgumentsJson,
                                            $"tool call '{toolCall.ToolCall.Name}' arguments")
                                    }
                            });
                        break;

                    case LlmToolResultContent toolResult:
                        parts.Add(
                            new GeminiContentPart
                            {
                                FunctionResponse =
                                    new GeminiFunctionResponse
                                    {
                                        Id = toolResult.Result.ToolCallId,
                                        Name = toolResult.Result.ToolName,
                                        Response = ToJsonValue(
                                            toolResult.Result.Content)
                                    }
                            });
                        break;
                }
            }

            if (parts.Count == 0)
                continue;

            contents.Add(
                new GeminiChatMessage
                {
                    Role = role,
                    Parts = parts
                });
        }

        var systemInstruction = systemText.Count == 0
            ? null
            : new GeminiSystemInstruction
            {
                Parts =
                [
                    new GeminiContentPart
                    {
                        Text = string.Join(
                            Environment.NewLine + Environment.NewLine,
                            systemText)
                    }
                ]
            };

        var responseSchema = request.ResponseFormat is null
            ? (JsonElement?)null
            : ParseJsonElement(
                request.ResponseFormat.Schema,
                "response format schema");

        var wireRequest = new GeminiChatRequest
        {
            Contents = contents,
            SystemInstruction = systemInstruction,
            GenerationConfig = new GeminiGenerationConfig
            {
                Temperature = request.Temperature,
                MaxOutputTokens = request.MaxTokens,
                ResponseSchema = responseSchema,
                ResponseMimeType =
                    responseSchema is null
                        ? null
                        : "application/json",
                ThinkingConfig = MapThinkingConfig(request.ThinkingConfig)
            },
            Tools = Capabilities.NativeToolCalling && request.Tools.Count > 0
                ? request.Tools
                    .Select(ToWireTool)
                    .ToList()
                : null
        };

        return wireRequest;
    }

    /// <summary>
    /// Maps an explicit thinking request to the <c>thinkingConfig</c> block.
    /// Enabling is expressed as a token budget; disabling as a zero budget.
    /// A missing effort with no configured budget cannot be expressed, so it
    /// is rejected rather than silently emitting no thinking configuration.
    /// </summary>
    private GeminiThinkingConfig? MapThinkingConfig(LlmThinkingConfig? config)
    {
        if (config is null || config.Mode == LlmThinkingMode.ProviderDefault)
        {
            return null;
        }

        if (config.Mode == LlmThinkingMode.Disabled)
        {
            return new GeminiThinkingConfig { ThinkingBudget = 0 };
        }

        return MapThinkingBudget(config.Effort) is { } budget
            ? new GeminiThinkingConfig { ThinkingBudget = budget }
            : throw new LlmRequestValidationException(
                $"Endpoint '{Model}' needs a Gemini thinking token budget " +
                "(set Capabilities.ThinkingBudget or request a concrete " +
                "effort instead of 'None').");
    }

    private static bool LooksLikeApiVersion(string segment) =>
        segment.Length >= 2 &&
        segment[0] == 'v' &&
        char.IsDigit(segment[1]);

    private int? MapThinkingBudget(LlmThinkingEffort effort) =>
        Capabilities.ThinkingBudget ?? EffortToBudget(effort);

    private static int? EffortToBudget(LlmThinkingEffort effort) =>
        effort switch
        {
            LlmThinkingEffort.Low => 1024,
            LlmThinkingEffort.Medium => 4096,
            LlmThinkingEffort.High => 16384,
            // Gemini has no explicit "max" tier; use the largest documented
            // budget (32768 for Gemini 2.5 Pro). Prefer an explicit
            // Capabilities.ThinkingBudget to match the exact model range.
            LlmThinkingEffort.Max => 32768,
            _ => null
        };

    /// <summary>
    /// Serializes tool-result text as a JSON value: valid JSON is preserved as
    /// an object/array, anything else is emitted as a JSON string.
    /// </summary>
    private static JsonElement ToJsonValue(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(text);
        }
    }

    private static GeminiTool ToWireTool(LlmTool tool)
    {
        return new GeminiTool
        {
            FunctionDeclarations = new List<GeminiFunctionDeclaration>
            {
                new GeminiFunctionDeclaration
                {
                    Name = tool.Name,
                    Description = tool.Description,
                    Parameters = ParseJsonElement(
                        tool.InputSchemaJson,
                        $"tool schema '{tool.Name}'")
                }
            }
        };
    }

    /// <inheritdoc />
    protected override async IAsyncEnumerable<LlmStreamEvent> ProcessStreamAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var receivedChunk = false;
        var receivedFinalChunk = false;
        var contentLength = 0;
        var nativeToolCallCount = 0;

        await foreach (var (_, data) in ReadSseEventsAsync(stream, cancellationToken))
        {
            receivedChunk = true;

            GeminiChatResponse? chunk;

            try
            {
                chunk = JsonSerializer.Deserialize<GeminiChatResponse>(
                    data,
                    JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new LlmClientException(
                    $"Gemini stream JSON parse error: {ex.Message}",
                    ex);
            }

            if (chunk is null)
                continue;

            if (chunk.Candidates is null || chunk.Candidates.Count == 0)
                continue;

            var candidate = chunk.Candidates[0];

            if (candidate.Content is null)
                continue;

            var content = candidate.Content;

            if (content.Parts is null)
                continue;

            foreach (var part in content.Parts)
            {
                var continuation = part.ThoughtSignature is null
                    ? null
                    : new LlmProviderContinuation(
                        Provider: "Gemini",
                        Values: new Dictionary<string, string>
                        {
                            ["thoughtSignature"] =
                                part.ThoughtSignature
                        });

                if (part.Text is not null)
                {
                    contentLength += part.Text.Length;

                    if (part.Thought == true)
                    {
                        yield return new LlmStreamEvent(
                            ReasoningContent: part.Text,
                            Continuation: continuation);
                    }
                    else
                    {
                        yield return new LlmStreamEvent(
                            Delta: part.Text,
                            Continuation: continuation);
                    }
                }

                if (part.FunctionCall is not null)
                {
                    var functionCall = part.FunctionCall;

                    yield return new LlmStreamEvent(
                        ToolCallDelta: new ToolCallDelta(
                            Index: nativeToolCallCount,
                            Id: functionCall.Id ??
                                Guid.NewGuid().ToString("N"),
                            Name: functionCall.Name,
                            ArgumentsJsonFragment:
                                functionCall.Args.ToString(),
                            Continuation: continuation),
                        Continuation: continuation);

                    nativeToolCallCount++;
                }
            }

            if (candidate.FinishReason is not null)
            {
                receivedFinalChunk = true;

                yield return new LlmStreamEvent(
                    FinishReason:
                        MapFinishReason(
                            candidate.FinishReason));
            }

            if (chunk.Usage is not null)
            {
                yield return new LlmStreamEvent(
                    Usage: new LlmUsage(
                        PromptTokens:
                            chunk.Usage.PromptTokenCount,
                        CompletionTokens:
                            chunk.Usage.CandidatesTokenCount,
                        TotalTokens:
                            chunk.Usage.TotalTokenCount));
            }
        }

        if (!receivedChunk)
            throw new LlmClientException(
                "Gemini stream returned no chunks.",
                LlmClientFailureKind.Availability);

        if (!receivedFinalChunk && contentLength == 0 && nativeToolCallCount == 0)
            throw new LlmClientException(
                "Gemini stream ended without a final chunk.",
                LlmClientFailureKind.Availability);
    }

    private static string MapFinishReason(string finishReason) =>
        finishReason switch
        {
            "STOP" => "stop",
            "MAX_OUTPUT_TOKENS" => "length",
            "SAFETY" => "content_filter",
            _ => finishReason.ToLowerInvariant()
        };
}