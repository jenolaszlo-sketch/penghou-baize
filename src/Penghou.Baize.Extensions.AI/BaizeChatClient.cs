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
    private readonly bool _ownsClient;
    private int _disposed;

    /// <summary>Initializes the adapter.</summary>
    public BaizeChatClient(
        ILlmClient client,
        string? providerName = null,
        Uri? providerUri = null,
        string? modelId = null,
        bool ownsClient = false)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _ownsClient = ownsClient;
        var clientMetadata = (client as ILlmClientMetadataProvider)?.Metadata;
        _metadata = new ChatClientMetadata(
            providerName ?? clientMetadata?.Provider,
            providerUri ?? clientMetadata?.Endpoint,
            modelId ?? clientMetadata?.Model);
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
                yield return Decorate(
                    new ChatResponseUpdate(ChatRole.Assistant, text),
                    item);

            if (item.ReasoningContent is { } reasoning)
            {
                yield return Decorate(
                    new ChatResponseUpdate(
                        ChatRole.Assistant,
                        [new TextReasoningContent(reasoning)]),
                    item);
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
                yield return Decorate(
                    new ChatResponseUpdate(
                        ChatRole.Assistant,
                        [new UsageContent(new UsageDetails
                        {
                            InputTokenCount = usage.PromptTokens,
                            OutputTokenCount = usage.CompletionTokens,
                            TotalTokenCount = usage.TotalTokens
                        })]),
                    item);
            }

            if (item.FinishReason is { } finishReason)
            {
                foreach (var call in MaterializeToolCalls(toolCalls))
                    yield return Decorate(call, item);
                toolCalls.Clear();

                yield return Decorate(new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    FinishReason = new ChatFinishReason(finishReason)
                }, item);
            }

            if (item.Diagnostics is not null ||
                item.RateLimit is not null ||
                item.RouterDiagnostics is not null)
            {
                yield return Decorate(
                    new ChatResponseUpdate { Role = ChatRole.Assistant },
                    item);
            }
        }

        foreach (var call in MaterializeToolCalls(toolCalls))
            yield return Decorate(call);
    }

    private IReadOnlyList<ChatResponseUpdate> MaterializeToolCalls(
        IReadOnlyDictionary<int, ToolCallBuilder> toolCalls)
    {
        var updates = new List<ChatResponseUpdate>(toolCalls.Count);
        foreach (var (_, call) in toolCalls.OrderBy(pair => pair.Key))
        {
            var arguments = ParseToolArguments(call.Arguments.ToString());
            updates.Add(new ChatResponseUpdate(
                ChatRole.Assistant,
                [new FunctionCallContent(
                    call.Id ?? Guid.NewGuid().ToString("N"),
                    call.Name ?? string.Empty,
                    arguments)]));
        }

        return updates;
    }

    private ChatResponseUpdate Decorate(
        ChatResponseUpdate update,
        LlmStreamEvent? raw = null)
    {
        update.ModelId = _metadata.DefaultModelId;
        update.RawRepresentation = raw;

        if (raw is not null)
        {
            var properties = new AdditionalPropertiesDictionary();
            if (raw.Diagnostics is not null)
                properties["baize.provider_diagnostics"] = raw.Diagnostics;
            if (raw.RouterDiagnostics is not null)
                properties["baize.router_diagnostics"] = raw.RouterDiagnostics;
            if (raw.RateLimit is not null)
                properties["baize.rate_limit"] = raw.RateLimit;
            if (properties.Count > 0)
                update.AdditionalProperties = properties;
        }

        return update;
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

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_ownsClient || Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        if (_client is IDisposable disposable)
            disposable.Dispose();
    }

    private static Dictionary<string, object?> ParseToolArguments(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(json) ?? [];
        }
        catch (JsonException)
        {
            // M.E.AI requires a materialized argument dictionary. Preserve the
            // provider bytes rather than terminating an otherwise valid stream.
            return new Dictionary<string, object?>
            {
                ["$raw"] = json
            };
        }
    }

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
        var responseFormat = options?.ResponseFormat is ChatResponseFormatJson json
            ? json.Schema is { } schema
                ? LlmResponseFormat.JsonSchema(schema.GetRawText())
                : LlmResponseFormat.Json()
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
                    SerializeToolResult(result.Result),
                    result.Exception is null)),
            _ => throw new NotSupportedException(
                $"Microsoft.Extensions.AI content '{content.GetType().Name}' is not supported by Baize.")
        };

    private static string SerializeToolResult(object? result) => result switch
    {
        null => "null",
        string value => value,
        JsonElement json => json.GetRawText(),
        _ => JsonSerializer.Serialize(result)
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
