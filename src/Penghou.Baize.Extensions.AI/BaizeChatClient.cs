using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Penghou.Baize.Extensions.AI;

/// <summary>Adapts a Baize client to the standard .NET <see cref="IChatClient"/>.</summary>
public sealed class BaizeChatClient : IChatClient
{
    private readonly ILlmClient _client;
    private readonly ChatClientMetadata _metadata;

    /// <summary>Initializes the adapter.</summary>
    public BaizeChatClient(
        ILlmClient client,
        string? providerName = null,
        Uri? providerUri = null,
        string? modelId = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _metadata = new ChatClientMetadata(providerName, providerUri, modelId);
    }

    /// <inheritdoc />
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        await GetStreamingResponseAsync(messages, options, cancellationToken)
            .ToChatResponseAsync(cancellationToken);

    /// <inheritdoc />
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var request = ToBaizeRequest(messages, options);
        var toolCalls = new Dictionary<int, ToolCallBuilder>();

        await foreach (var item in _client.StreamAsync(request, cancellationToken))
        {
            if (item.Delta is { } text)
                yield return new ChatResponseUpdate(ChatRole.Assistant, text);

            if (item.ReasoningContent is { } reasoning)
            {
                yield return new ChatResponseUpdate(
                    ChatRole.Assistant,
                    [new TextReasoningContent(reasoning)]);
            }

            if (item.ToolCallDelta is { } delta)
            {
                if (!toolCalls.TryGetValue(delta.Index, out var builder))
                {
                    builder = new ToolCallBuilder();
                    toolCalls[delta.Index] = builder;
                }

                builder.Id ??= delta.Id;
                builder.Name ??= delta.Name;
                if (delta.ArgumentsJsonFragment is { } fragment)
                    builder.Arguments.Append(fragment);
            }

            if (item.Usage is { } usage)
            {
                yield return new ChatResponseUpdate(
                    ChatRole.Assistant,
                    [new UsageContent(new UsageDetails
                    {
                        InputTokenCount = usage.PromptTokens,
                        OutputTokenCount = usage.CompletionTokens,
                        TotalTokenCount = usage.TotalTokens
                    })]);
            }

            if (item.FinishReason is { } finishReason)
            {
                yield return new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    FinishReason = new ChatFinishReason(finishReason)
                };
            }
        }

        foreach (var (_, call) in toolCalls.OrderBy(pair => pair.Key))
        {
            var arguments = string.IsNullOrWhiteSpace(call.Arguments.ToString())
                ? new Dictionary<string, object?>()
                : JsonSerializer.Deserialize<Dictionary<string, object?>>(
                    call.Arguments.ToString()) ?? [];
            yield return new ChatResponseUpdate(
                ChatRole.Assistant,
                [new FunctionCallContent(
                    call.Id ?? Guid.NewGuid().ToString("N"),
                    call.Name ?? string.Empty,
                    arguments)]);
        }
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (serviceKey is not null)
            return null;
        if (serviceType.IsInstanceOfType(this))
            return this;
        if (serviceType == typeof(ChatClientMetadata))
            return _metadata;
        if (serviceType.IsInstanceOfType(_client))
            return _client;
        return null;
    }

    /// <summary>The adapter owns no disposable provider resources.</summary>
    public void Dispose() { }

    private LlmRequest ToBaizeRequest(
        IEnumerable<ChatMessage> source,
        ChatOptions? options)
    {
        var sourceMessages = source.ToArray();
        var callNames = sourceMessages
            .SelectMany(message => message.Contents)
            .OfType<FunctionCallContent>()
            .ToDictionary(call => call.CallId, call => call.Name, StringComparer.Ordinal);
        var messages = new List<LlmMessage>();

        if (!string.IsNullOrWhiteSpace(options?.Instructions))
            messages.Add(new LlmMessage("system", options.Instructions));

        messages.AddRange(sourceMessages.Select(message => new LlmMessage(
            message.Role.Value,
            message.Contents.Select(content => ToBaizeContent(content, callNames)).ToArray())));

        var tools = options?.Tools?
            .OfType<AIFunctionDeclaration>()
            .Select(tool => new LlmTool(
                tool.Name,
                tool.Description ?? string.Empty,
                tool.JsonSchema.GetRawText()))
            .ToList();
        var responseFormat = options?.ResponseFormat is ChatResponseFormatJson json &&
            json.Schema is { } schema
                ? LlmResponseFormat.JsonSchema(schema.GetRawText())
                : null;

        return new LlmRequest(
            messages,
            options?.Temperature,
            options?.MaxOutputTokens,
            tools,
            responseFormat,
            ToThinkingConfig(options?.Reasoning));
    }

    private LlmContentPart ToBaizeContent(
        AIContent content,
        IReadOnlyDictionary<string, string> callNames) => content switch
        {
            TextContent text => new LlmTextContent(text.Text),
            TextReasoningContent reasoning => new LlmReasoningContent(reasoning.Text),
            DataContent data => ToMedia(
                data.MediaType,
                new LlmInlineDataSource(data.Data),
                data.Name),
            UriContent uri => ToMedia(
                uri.MediaType,
                new LlmUriSource(uri.Uri),
                null),
            HostedFileContent file => ToMedia(
                file.MediaType ?? "application/octet-stream",
                new LlmProviderFileSource(
                    new LlmProviderKey(_metadata.ProviderName ?? "Unknown"),
                    file.FileId),
                file.Name),
            FunctionCallContent call => new LlmToolCallContent(
                new LlmToolCall(
                    call.CallId,
                    call.Name,
                    JsonSerializer.Serialize(call.Arguments))),
            FunctionResultContent result => new LlmToolResultContent(
                new LlmToolResult(
                    result.CallId,
                    callNames.GetValueOrDefault(result.CallId) ?? string.Empty,
                    JsonSerializer.Serialize(result.Result),
                    result.Exception is null)),
            _ => throw new NotSupportedException(
                $"Microsoft.Extensions.AI content '{content.GetType().Name}' is not supported by Baize.")
        };

    private static LlmMediaContent ToMedia(
        string mediaType,
        LlmMediaSource source,
        string? name) => mediaType.Split('/', 2)[0].ToLowerInvariant() switch
        {
            "image" => new LlmImageContent(mediaType, source),
            "audio" => new LlmAudioContent(mediaType, source),
            "video" => new LlmVideoContent(mediaType, source),
            _ => new LlmFileContent(mediaType, source, name)
        };

    private static LlmThinkingConfig? ToThinkingConfig(ReasoningOptions? reasoning)
    {
        if (reasoning?.Effort is not { } effort)
            return null;

        return new LlmThinkingConfig(
            effort == ReasoningEffort.None
                ? LlmThinkingMode.Disabled
                : LlmThinkingMode.Enabled,
            effort switch
            {
                ReasoningEffort.Low => LlmThinkingEffort.Low,
                ReasoningEffort.Medium => LlmThinkingEffort.Medium,
                ReasoningEffort.High => LlmThinkingEffort.High,
                ReasoningEffort.ExtraHigh => LlmThinkingEffort.Max,
                _ => LlmThinkingEffort.None
            });
    }

    private sealed class ToolCallBuilder
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public StringBuilder Arguments { get; } = new();
    }
}
