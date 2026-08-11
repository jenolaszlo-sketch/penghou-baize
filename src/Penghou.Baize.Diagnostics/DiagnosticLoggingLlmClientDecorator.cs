using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace Penghou.Baize.Diagnostics;

internal sealed class DiagnosticLoggingLlmClientDecorator(
    ILogger<DiagnosticLoggingLlmClientDecorator> logger) : ILlmClientDecorator
{
    public ILlmClient Decorate(ILlmClient client) =>
        client is DiagnosticLoggingLlmClient
            ? client
            : new DiagnosticLoggingLlmClient(client, logger);

    private sealed class DiagnosticLoggingLlmClient(
        ILlmClient inner,
        ILogger logger) : ILlmClient, ILlmClientMetadataProvider
    {
        public LlmEndpointCapabilities Capabilities => inner.Capabilities;

        public LlmClientMetadata Metadata =>
            (inner as ILlmClientMetadataProvider)?.Metadata ??
            new LlmClientMetadata("Unknown", "Unknown");

        public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
            LlmRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var events = 0;
            var contentCharacters = 0;
            var reasoningCharacters = 0;
            var toolFragments = 0;
            LlmUsage? usage = null;
            string? finishReason = null;
            var succeeded = false;

            logger.LogDebug(
                "Starting Baize stream for endpoint {EndpointId}, provider " +
                "{Provider}, model {Model}, messages {MessageCount}, tools " +
                "{ToolCount}",
                Metadata.EndpointId,
                Metadata.Provider,
                Metadata.Model,
                request.Messages.Count,
                request.Tools.Count);
            try
            {
                await using var enumerator = inner.StreamAsync(request, cancellationToken)
                    .GetAsyncEnumerator(cancellationToken);
                while (true)
                {
                    LlmStreamEvent item;
                    try
                    {
                        if (!await enumerator.MoveNextAsync())
                            break;
                        item = enumerator.Current;
                    }
                    catch (Exception exception)
                    {
                        LogFailure(exception);
                        throw;
                    }

                    events++;
                    contentCharacters += item.Delta?.Length ?? 0;
                    reasoningCharacters += item.ReasoningContent?.Length ?? 0;
                    if (item.ToolCallDelta is not null)
                        toolFragments++;
                    usage = item.Usage ?? usage;
                    finishReason = item.FinishReason ?? finishReason;
                    yield return item;
                }

                succeeded = true;
            }
            finally
            {
                logger.LogDebug(
                    "Completed Baize stream for endpoint {EndpointId}, provider " +
                    "{Provider}, model {Model}, succeeded {Succeeded}, events " +
                    "{EventCount}, content characters {ContentCharacters}, " +
                    "reasoning characters {ReasoningCharacters}, tool fragments " +
                    "{ToolFragments}, finish reason {FinishReason}, prompt tokens " +
                    "{PromptTokens}, completion tokens {CompletionTokens}",
                    Metadata.EndpointId,
                    Metadata.Provider,
                    Metadata.Model,
                    succeeded,
                    events,
                    contentCharacters,
                    reasoningCharacters,
                    toolFragments,
                    finishReason,
                    usage?.PromptTokens,
                    usage?.CompletionTokens);
            }
        }

        private void LogFailure(Exception exception)
        {
            logger.LogWarning(
                "Baize stream failed for endpoint {EndpointId}, provider " +
                "{Provider}, model {Model}, error type {ErrorType}, HTTP " +
                "status {StatusCode}. Inspect correlated HTTP diagnostics " +
                "for provider content",
                Metadata.EndpointId,
                Metadata.Provider,
                Metadata.Model,
                exception.GetType().FullName,
                (exception as LlmClientException)?.StatusCode);
        }
    }
}
