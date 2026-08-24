using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Penghou.Baize;

namespace Penghou.Baize.Ollama;

/// <summary>
/// Native Ollama /api/chat client. Ollama emits one JSON object per line;
/// content is forwarded as it arrives, while native tool calls and final
/// usage data are mapped to the canonical ILlmClient event stream.
/// </summary>
public sealed class OllamaChatClient : LlmClientBase
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
    /// Creates an Ollama chat client.
    /// </summary>
    /// <param name="model">The Ollama model identifier (for example <c>qwen2.5-coder</c>).</param>
    /// <param name="httpClientFactory">Factory providing the underlying <see cref="HttpClient"/>.</param>
    /// <param name="apiKey">Optional API key; attached as a Bearer token when provided.</param>
    /// <param name="baseUrl">Base API URL, for example <c>http://localhost:11434/api</c>.</param>
    /// <param name="capabilities">The declared capabilities of the endpoint.</param>
    public OllamaChatClient(
        string model,
        IHttpClientFactory httpClientFactory,
        string apiKey,
        string baseUrl,
        LlmEndpointCapabilities capabilities)
        : base(model, httpClientFactory, apiKey, capabilities, "Ollama")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);

        var normalizedBaseUrl =
            baseUrl.TrimEnd('/');
        var chatUrl =
            normalizedBaseUrl.EndsWith(
                "/api",
                StringComparison.OrdinalIgnoreCase)
                ? $"{normalizedBaseUrl}/chat"
                : $"{normalizedBaseUrl}/api/chat";

        _chatUri = new Uri(chatUrl);
    }

    /// <inheritdoc />
    /// <inheritdoc />
    protected override void ApplyAuth(HttpRequestMessage httpRequest)
    {
        if (!string.IsNullOrWhiteSpace(ApiKey))
        {
            httpRequest.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    ApiKey);
        }
    }

    /// <inheritdoc />
    protected override HttpRequestMessage CreateHttpRequest(LlmRequest request)
    {
        var wireRequest = ToWireRequest(request);
        var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            _chatUri);

        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(
                wireRequest,
                JsonOptions),
            Encoding.UTF8,
            "application/json");

        return httpRequest;
    }

    /// <summary>Maps the neutral request onto the Ollama wire format.</summary>
    private OllamaChatRequest ToWireRequest(LlmRequest request)
    {
        var tools = !Capabilities.NativeToolCalling ||
                    request.Tools.Count == 0
            ? null
            : request.Tools
                .Select(tool =>
                    new OllamaTool
                    {
                        Function =
                            new OllamaFunctionDefinition
                            {
                                Name = tool.Name,
                                Description =
                                    tool.Description,
                                Parameters =
                                    ParseJsonElement(
                                        tool.InputSchemaJson,
                                        $"tool schema '{tool.Name}'")
                            }
                    })
                .ToArray();
        var options =
            request.Temperature is null &&
            request.MaxTokens is null
                ? null
                : new OllamaOptions
                {
                    Temperature =
                        request.Temperature,
                    NumPredict =
                        request.MaxTokens
                };

        return new OllamaChatRequest
        {
            Model = Model,
            Messages = request.Messages
                .SelectMany(ToWireMessages)
                .ToArray(),
            Stream = true,
            Tools = tools,
            Options = options,
            Format = request.ResponseFormat is null
                ? null
                : request.ResponseFormat.Schema is null
                    ? "json"
                    : ParseJsonElement(
                        request.ResponseFormat.Schema,
                        "response format schema")
        };
    }

    private IEnumerable<OllamaMessage> ToWireMessages(LlmMessage message)
    {
        if (string.Equals(message.Role, "tool", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var result in message.Parts
                .OfType<LlmToolResultContent>()
                .Select(part => part.Result))
            {
                yield return new OllamaMessage
                {
                    Role = "tool",
                    Content = result.Content
                };
            }

            yield break;
        }

        var text = string.Concat(
            message.Parts
                .OfType<LlmTextContent>()
                .Select(part => part.Text));
        var toolCalls = message.Parts
            .OfType<LlmToolCallContent>()
            .Select(part => part.ToolCall)
            .ToList();
        var images = message.Parts
            .OfType<LlmImageContent>()
            .Select(image => image.Source switch
            {
                LlmInlineDataSource inline =>
                    Convert.ToBase64String(inline.Data.Span),
                _ => throw new LlmRequestValidationException(
                    "Ollama supports image inputs only as inline data.")
            })
            .ToList();

        var unsupportedMedia = message.Parts
            .OfType<LlmMediaContent>()
            .FirstOrDefault(media => media is not LlmImageContent);
        if (unsupportedMedia is not null)
        {
            throw new LlmRequestValidationException(
                $"Ollama does not support content type " +
                $"'{unsupportedMedia.GetType().Name}'.");
        }

        if (toolCalls.Count == 0 && images.Count == 0 && string.IsNullOrEmpty(text))
            yield break;

        yield return new OllamaMessage
        {
            Role = message.Role,
            Content = string.IsNullOrEmpty(text) ? null : text,
            Images = images.Count == 0 ? null : images,
            ToolCalls = toolCalls.Count == 0
                ? null
                : toolCalls
                    .Select(call => new OllamaToolCall
                    {
                        Type = "function",
                        Function = new OllamaCalledFunction
                        {
                            Name = call.Name,
                            Arguments = ParseJsonElement(
                                call.ArgumentsJson,
                                $"tool call '{call.Name}' arguments")
                        }
                    })
                    .ToList()
        };
    }

    /// <inheritdoc />
    protected override async IAsyncEnumerable<LlmStreamEvent> ProcessStreamAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream);
        var receivedChunk = false;
        var receivedFinalChunk = false;
        var contentLength = 0;
        var nativeToolCallCount = 0;
        var nextPartIndex = 0;
        int? contentPartIndex = null;
        var toolPartIndices = new Dictionary<int, int>();

        while (true)
        {
            var line =
                await reader.ReadLineAsync(
                    cancellationToken);

            if (line is null)
                break;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            receivedChunk = true;
            var chatResponse =
                ParseResponseChunk(line);

            if (!string.IsNullOrEmpty(
                    chatResponse.Message?.Content))
            {
                contentPartIndex ??= nextPartIndex++;
                contentLength +=
                    chatResponse.Message.Content.Length;

                yield return new LlmStreamEvent(
                    Delta:
                        chatResponse.Message.Content)
                {
                    PartIndex = contentPartIndex
                };
            }

            if (chatResponse.Message?.ToolCalls is
                { Count: > 0 } toolCalls)
            {
                nativeToolCallCount +=
                    toolCalls.Count;

                for (var position = 0;
                     position < toolCalls.Count;
                     position++)
                {
                    var toolCall =
                        toolCalls[position];
                    var toolIndex = toolCall.Function.Index ?? position;

                    if (!toolPartIndices.TryGetValue(toolIndex, out var partIndex))
                    {
                        partIndex = nextPartIndex++;
                        toolPartIndices[toolIndex] = partIndex;
                    }

                    yield return new LlmStreamEvent(
                        ToolCallDelta:
                            new ToolCallDelta(
                                Index:
                                    toolIndex,
                                Id: null,
                                Name:
                                    toolCall.Function.Name,
                                ArgumentsJsonFragment:
                                    GetArgumentsJson(
                                        toolCall.Function
                                            .Arguments)))
                    {
                        PartIndex = partIndex
                    };
                }
            }

            if (!chatResponse.Done)
                continue;

            receivedFinalChunk = true;

            yield return new LlmStreamEvent(
                FinishReason:
                    chatResponse.DoneReason ??
                    "stop",
                Usage:
                    CreateUsage(chatResponse),
                Diagnostics:
                    CreateDiagnostics(
                        chatResponse,
                        nativeToolCallCount,
                        contentLength));
        }

        if (!receivedChunk)
        {
            throw new LlmClientException(
                "Ollama chat response stream was empty.",
                LlmClientFailureKind.Availability);
        }

        if (!receivedFinalChunk)
        {
            throw new LlmClientException(
                "Ollama chat response stream ended before a final chunk was received.",
                LlmClientFailureKind.Availability);
        }
    }

    private static OllamaChatResponse
        ParseResponseChunk(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<
                       OllamaChatResponse>(
                       line,
                       JsonOptions) ??
                    throw new LlmClientException(
                        "Ollama chat response chunk was empty.");
        }
        catch (JsonException ex)
        {
            throw new LlmClientException(
                $"Failed to parse Ollama chat response chunk: {line}",
                ex);
        }
    }

    private static string GetArgumentsJson(
        JsonElement arguments) =>
        arguments.ValueKind switch
        {
            JsonValueKind.String =>
                arguments.GetString() ??
                string.Empty,
            JsonValueKind.Undefined or
            JsonValueKind.Null =>
                "{}",
            _ => arguments.GetRawText()
        };

    private static LlmUsage? CreateUsage(
        OllamaChatResponse response)
    {
        if (response.PromptEvalCount is null &&
            response.EvalCount is null)
        {
            return null;
        }

        return new LlmUsage(
            PromptTokens:
                response.PromptEvalCount,
            CompletionTokens:
                response.EvalCount,
            TotalTokens:
                (response.PromptEvalCount ?? 0) +
                (response.EvalCount ?? 0));
    }

    private static LlmProviderDiagnostics CreateDiagnostics(
        OllamaChatResponse response,
        int nativeToolCallCount,
        int contentLength)
    {
        double? generationTokensPerSecond =
            response.EvalCount is { } tokenCount &&
            response.EvalDuration is > 0
                ? tokenCount * 1_000_000_000d /
                  response.EvalDuration.Value
                : null;

        return new LlmProviderDiagnostics(
            Provider: "Ollama",
            ActualModel: response.Model,
            Api: "native",
            Done: response.Done,
            DoneReason: response.DoneReason,
            TotalDurationMilliseconds:
                ToMilliseconds(response.TotalDuration),
            LoadDurationMilliseconds:
                ToMilliseconds(response.LoadDuration),
            PromptEvaluationDurationMilliseconds:
                ToMilliseconds(
                    response.PromptEvalDuration),
            GenerationDurationMilliseconds:
                ToMilliseconds(response.EvalDuration),
            GenerationTokensPerSecond:
                generationTokensPerSecond,
            NativeToolCallCount:
                nativeToolCallCount,
            ContentLength:
                contentLength);
    }

    private static double? ToMilliseconds(
        long? nanoseconds) =>
        nanoseconds is null
            ? null
            : nanoseconds.Value / 1_000_000d;
}
