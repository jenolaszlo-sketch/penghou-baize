using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using Penghou.Model.Abstractions;

namespace Penghou.Baize;

internal static class BaizeModelTransport
{
    private const int MaximumMessages = 1024;
    private const int MaximumParts = 8192;
    private const int MaximumMetadataEntries = 32;
    private const int MaximumTextBytes = 4 * 1024 * 1024;
    private const long MaximumPayloadBytes = 64L * 1024 * 1024;
    private const int MaximumInlineBytes = 64 * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static LlmRequest Snapshot(LlmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.TransportAttempt < 1)
            throw new ArgumentOutOfRangeException(nameof(request), "Transport attempt must be at least one.");
        if (request.Messages.Count > MaximumMessages)
            throw new ArgumentException($"Model requests may contain at most {MaximumMessages} messages.", nameof(request));
        if (request.Tools.Count > 256)
            throw new ArgumentException("Model requests may contain at most 256 tool declarations.", nameof(request));
        if (request.Metadata.Count > MaximumMetadataEntries)
            throw new ArgumentException($"Model request metadata is limited to {MaximumMetadataEntries} entries.", nameof(request));

        long payloadBytes = 0;
        var partCount = 0;
        var messages = new List<LlmMessage>(request.Messages.Count);
        foreach (var message in request.Messages)
        {
            if (message is null)
                throw new ArgumentException("Model requests cannot contain null messages.", nameof(request));
            AddString(message.Role, "message role", ref payloadBytes, 256);
            var parts = new List<LlmContentPart>(message.Parts.Count);
            foreach (var part in message.Parts)
            {
                if (++partCount > MaximumParts)
                    throw new ArgumentException($"Model requests may contain at most {MaximumParts} content parts.", nameof(request));
                parts.Add(SnapshotPart(part, ref payloadBytes));
            }
            messages.Add(new LlmMessage(message.Role, parts.AsReadOnly()));
        }

        var tools = new List<LlmTool>(request.Tools.Count);
        foreach (var tool in request.Tools)
        {
            if (tool is null)
                throw new ArgumentException("Model requests cannot contain null tool declarations.", nameof(request));
            AddString(tool.Name, "tool name", ref payloadBytes, 1024);
            AddString(tool.Description, "tool description", ref payloadBytes, MaximumTextBytes);
            AddString(tool.InputSchemaJson, "tool schema", ref payloadBytes, MaximumTextBytes);
            tools.Add(tool with { });
        }

        var metadata = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var pair in request.Metadata)
        {
            AddString(pair.Key, "metadata key", ref payloadBytes, 256);
            metadata.Add(pair.Key, SnapshotMetadataValue(pair.Value, ref payloadBytes));
        }

        var responseFormat = request.ResponseFormat switch
        {
            null => null,
            { Type: "json_object", Schema: null } => LlmResponseFormat.Json(),
            { Type: "json_schema", Schema: not null } format => LlmResponseFormat.JsonSchema(
                CopyString(format.Schema, "response schema", ref payloadBytes, MaximumTextBytes)),
            _ => throw new ArgumentException("The response format is outside the supported immutable data profile.", nameof(request))
        };
        var thinking = request.ThinkingConfig is null
            ? null
            : new LlmThinkingConfig(request.ThinkingConfig.Mode, request.ThinkingConfig.Effort);

        return new LlmRequest(
            messages.AsReadOnly(),
            request.Temperature,
            request.MaxTokens,
            tools.AsReadOnly(),
            responseFormat,
            thinking,
            new ReadOnlyDictionary<string, object?>(metadata))
        {
            ExecutionContext = request.ExecutionContext,
            UsageIntent = request.UsageIntent,
            TransportAttempt = request.TransportAttempt
        };
    }

    internal static ModelInvocation CreateInvocation(
        ModelTarget target,
        ModelOperation operation,
        LlmRequest request,
        string? requestId = null,
        int? attempt = null) => new(
            requestId ?? Guid.NewGuid().ToString("N"),
            target,
            operation,
            request.ExecutionContext,
            request.UsageIntent,
            attempt: attempt ?? request.TransportAttempt);

    internal static ModelUsage? ToModelUsage(LlmUsage? usage) => usage is null
        ? null
        : new ModelUsage(
            inputTokens: usage.PromptTokens,
            outputTokens: usage.CompletionTokens,
            cachedInputTokens: usage.PromptCacheHitTokens,
            thinkingTokens: usage.ThinkingTokens,
            totalTokens: usage.TotalTokens,
            cacheMissInputTokens: usage.PromptCacheMissTokens,
            cost: usage.Cost,
            currency: usage.Currency);

    internal static LlmUsage? ToLlmUsage(ModelUsage? usage) => usage is null
        ? null
        : new LlmUsage(
            PromptTokens: ToInt32(usage.InputTokens),
            CompletionTokens: ToInt32(usage.OutputTokens),
            TotalTokens: ToInt32(usage.TotalTokens),
            PromptCacheHitTokens: ToInt32(usage.CachedInputTokens),
            PromptCacheMissTokens: ToInt32(usage.CacheMissInputTokens),
            ThinkingTokens: ToInt32(usage.ThinkingTokens))
        {
            Cost = usage.Cost,
            Currency = usage.Currency
        };

    internal static void VerifyResponseRequestId(ModelInvocation invocation, string responseRequestId)
    {
        if (!string.Equals(invocation.RequestId, responseRequestId, StringComparison.Ordinal))
            throw new LlmClientException("The model transport returned a response for a different request.", LlmClientFailureKind.Protocol);
    }

    private static LlmContentPart SnapshotPart(LlmContentPart part, ref long budget)
    {
        ArgumentNullException.ThrowIfNull(part);
        LlmContentPart result;
        if (part.GetType() == typeof(LlmTextContent))
        {
            var value = (LlmTextContent)part;
            result = new LlmTextContent(CopyString(value.Text, "text content", ref budget, MaximumTextBytes));
        }
        else if (part.GetType() == typeof(LlmReasoningContent))
        {
            var value = (LlmReasoningContent)part;
            result = new LlmReasoningContent(CopyString(value.Text, "reasoning content", ref budget, MaximumTextBytes));
        }
        else if (part.GetType() == typeof(LlmToolCallContent))
        {
            result = new LlmToolCallContent(SnapshotToolCall(((LlmToolCallContent)part).ToolCall, ref budget));
        }
        else if (part.GetType() == typeof(LlmToolResultContent))
        {
            var value = ((LlmToolResultContent)part).Result;
            result = new LlmToolResultContent(value with
            {
                ToolCallId = CopyString(value.ToolCallId, "tool call identifier", ref budget, 1024),
                ToolName = CopyString(value.ToolName, "tool name", ref budget, 1024),
                Content = CopyString(value.Content, "tool result", ref budget, MaximumTextBytes)
            });
        }
        else if (part is LlmMediaContent media && media.GetType() is var type &&
                 (type == typeof(LlmImageContent) || type == typeof(LlmAudioContent) ||
                  type == typeof(LlmVideoContent) || type == typeof(LlmFileContent)))
        {
            var mediaType = CopyString(media.MediaType, "media type", ref budget, 256);
            var source = SnapshotSource(media.Source, ref budget);
            result = type == typeof(LlmImageContent) ? new LlmImageContent(mediaType, source)
                : type == typeof(LlmAudioContent) ? new LlmAudioContent(mediaType, source)
                : type == typeof(LlmVideoContent) ? new LlmVideoContent(mediaType, source)
                : new LlmFileContent(mediaType, source,
                    ((LlmFileContent)media).FileName is { } name
                        ? CopyString(name, "file name", ref budget, 1024)
                        : null);
        }
        else
        {
            throw new ArgumentException($"Content type '{part.GetType().Name}' is outside the supported immutable data profile.", nameof(part));
        }
        return result with { Continuation = SnapshotContinuation(part.Continuation, ref budget) };
    }

    private static LlmMediaSource SnapshotSource(LlmMediaSource source, ref long budget)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.GetType() == typeof(LlmInlineDataSource))
        {
            var data = ((LlmInlineDataSource)source).Data;
            if (data.Length > MaximumInlineBytes)
                throw new ArgumentException("Inline media exceeds the supported 64 MiB profile.", nameof(source));
            AddBytes(data.Length, ref budget);
            return new LlmInlineDataSource(data);
        }
        if (source.GetType() == typeof(LlmUriSource))
        {
            var uri = ((LlmUriSource)source).Uri;
            if (uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
                throw new ArgumentException("Media URIs must use HTTP(S) and contain no user info or fragment.", nameof(source));
            AddString(uri.AbsoluteUri, "media URI", ref budget, MaximumTextBytes);
            return new LlmUriSource(new Uri(uri.AbsoluteUri, UriKind.Absolute));
        }
        if (source.GetType() == typeof(LlmProviderFileSource))
        {
            var file = (LlmProviderFileSource)source;
            return new LlmProviderFileSource(file.Provider,
                CopyString(file.FileId, "provider file identifier", ref budget, 1024));
        }
        throw new ArgumentException($"Media source type '{source.GetType().Name}' is outside the supported immutable data profile.", nameof(source));
    }

    private static LlmToolCall SnapshotToolCall(LlmToolCall call, ref long budget)
    {
        ArgumentNullException.ThrowIfNull(call);
        var attempts = SnapshotAttempts(call.JsonRepairAttempts, ref budget);
        var diagnostics = SnapshotDiagnostics(call.JsonRepairDiagnostics, ref budget);
        var validation = SnapshotValidation(call.ArgumentValidation, ref budget);
        return call with
        {
            Id = CopyString(call.Id, "tool call identifier", ref budget, 1024),
            Name = CopyString(call.Name, "tool name", ref budget, 1024),
            ArgumentsJson = CopyString(call.ArgumentsJson, "tool arguments", ref budget, MaximumTextBytes),
            JsonRepairAttempts = attempts,
            Continuation = SnapshotContinuation(call.Continuation, ref budget),
            JsonRepairDiagnostics = diagnostics,
            ArgumentValidation = validation
        };
    }

    private static LlmProviderContinuation? SnapshotContinuation(LlmProviderContinuation? value, ref long budget)
    {
        if (value is null) return null;
        if (value.Values.Count > 32) throw new ArgumentException("Provider continuation metadata is limited to 32 entries.");
        AddString(value.Provider, "continuation provider", ref budget, 256);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in value.Values)
        {
            AddString(pair.Key, "continuation key", ref budget, 64);
            AddString(pair.Value, "continuation value", ref budget, 1024);
            values.Add(pair.Key, pair.Value);
        }
        if (values.Count != value.Values.Count) throw new ArgumentException("Provider continuation metadata count changed during snapshot.");
        return new LlmProviderContinuation(value.Provider,
            new ReadOnlyDictionary<string, string>(values));
    }

    private static IReadOnlyList<LlmRepairAttempt>? SnapshotAttempts(IReadOnlyList<LlmRepairAttempt>? values, ref long budget)
    {
        if (values is null) return null;
        if (values.Count > 32) throw new ArgumentException("Repair attempt lists are limited to 32 entries.");
        var copy = new List<LlmRepairAttempt>(values.Count);
        foreach (var value in values)
        {
            if (value is null) throw new ArgumentException("Repair attempts cannot contain null entries.");
            copy.Add(value with
            {
                Name = CopyString(value.Name, "repair strategy name", ref budget, 256),
                Repaired = value.Repaired is null ? null : CopyString(value.Repaired, "repaired value", ref budget, MaximumTextBytes),
                Note = value.Note is null ? null : CopyString(value.Note, "repair note", ref budget, 4096)
            });
        }
        return copy.AsReadOnly();
    }

    private static LlmJsonRepairDiagnostics? SnapshotDiagnostics(LlmJsonRepairDiagnostics? value, ref long budget)
    {
        if (value is null) return null;
        var errors = SnapshotStrings(value.ShapeErrors, "repair error", ref budget, 4096);
        var tolerant = value.TolerantRecovery is null ? null : value.TolerantRecovery with
        {
            Outcome = CopyString(value.TolerantRecovery.Outcome, "recovery outcome", ref budget, 256),
            Corrections = SnapshotStrings(value.TolerantRecovery.Corrections, "recovery correction", ref budget, 4096)
        };
        return value with
        {
            ShapeErrors = errors,
            SucceededBy = value.SucceededBy is null ? null : CopyString(value.SucceededBy, "repair strategy", ref budget, 256),
            TolerantRecovery = tolerant
        };
    }

    private static LlmToolArgumentValidationResult? SnapshotValidation(LlmToolArgumentValidationResult? value, ref long budget)
    {
        if (value is null) return null;
        if (value.Failures.Count > 256) throw new ArgumentException("Tool validation failures are limited to 256 entries.");
        var failures = new List<LlmToolArgumentValidationFailure>(value.Failures.Count);
        foreach (var failure in value.Failures)
        {
            failures.Add(failure with
            {
                Path = CopyString(failure.Path, "validation path", ref budget, 1024),
                Code = CopyString(failure.Code, "validation code", ref budget, 256),
                Message = CopyString(failure.Message, "validation message", ref budget, 4096)
            });
        }
        return value with
        {
            ValidatorId = CopyString(value.ValidatorId, "validator identifier", ref budget, 256),
            Failures = failures.AsReadOnly(),
            UnsupportedKeywords = value.UnsupportedKeywords is null
                ? null
                : SnapshotStrings(value.UnsupportedKeywords, "unsupported schema keyword", ref budget, 256)
        };
    }

    private static IReadOnlyList<string> SnapshotStrings(IReadOnlyList<string> values, string description, ref long budget, int maximumBytes)
    {
        if (values.Count > 256) throw new ArgumentException("Diagnostic string collections are limited to 256 entries.");
        var copy = new List<string>(values.Count);
        foreach (var value in values)
            copy.Add(CopyString(value, description, ref budget, maximumBytes));
        return copy.AsReadOnly();
    }

    private static object? SnapshotMetadataValue(object? value, ref long budget) => value switch
    {
        null => null,
        string text => CopyString(text, "metadata value", ref budget, 4096),
        bool or byte or sbyte or short or ushort or int or uint or long or ulong or decimal or char => value,
        float f when float.IsFinite(f) => f,
        double d when double.IsFinite(d) => d,
        JsonElement element => SnapshotJson(element, ref budget),
        _ => throw new ArgumentException($"Metadata type '{value.GetType().Name}' is outside the supported immutable data profile.", nameof(value))
    };

    private static JsonElement SnapshotJson(JsonElement element, ref long budget)
    {
        var json = element.GetRawText();
        AddString(json, "metadata JSON", ref budget, MaximumTextBytes);
        using var parsed = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
        return parsed.RootElement.Clone();
    }

    private static int? ToInt32(long? value) => value switch
    {
        null => null,
        < int.MinValue => throw new LlmClientException("Reported token usage exceeds the Baize token range.", LlmClientFailureKind.Protocol),
        > int.MaxValue => throw new LlmClientException("Reported token usage exceeds the Baize token range.", LlmClientFailureKind.Protocol),
        _ => (int)value.Value
    };

    private static string CopyString(string value, string description, ref long budget, int maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(value);
        AddString(value, description, ref budget, maximumBytes);
        return value;
    }

    private static void AddString(string value, string description, ref long budget, int maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > maximumBytes)
            throw new ArgumentException($"{description} exceeds its supported data-profile limit.");
        int bytes;
        try { bytes = StrictUtf8.GetByteCount(value); }
        catch (EncoderFallbackException exception) { throw new ArgumentException($"{description} contains malformed Unicode.", exception); }
        if (bytes > maximumBytes)
            throw new ArgumentException($"{description} exceeds its supported data-profile limit.");
        AddBytes(bytes, ref budget);
    }

    private static void AddBytes(long count, ref long budget)
    {
        budget = checked(budget + count);
        if (budget > MaximumPayloadBytes)
            throw new ArgumentException("Model payload exceeds the 64 MiB supported data profile.");
    }
}
