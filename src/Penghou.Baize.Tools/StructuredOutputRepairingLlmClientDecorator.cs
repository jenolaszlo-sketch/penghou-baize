using System.Runtime.CompilerServices;
using System.Text;

namespace Penghou.Baize.Tools;

/// <summary>
/// Opt-in endpoint decorator that buffers schema-constrained content, repairs
/// it deterministically, and then releases the response stream. Ordinary text,
/// tool-only, and schema-less requests retain their original streaming path.
/// </summary>
public sealed class StructuredOutputRepairingLlmClientDecorator(
    ILlmStructuredOutputRepairer repairer) : ILlmClientDecorator
{
    /// <inheritdoc />
    public ILlmClient Decorate(ILlmClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        return client is StructuredOutputRepairingLlmClient
            ? client
            : new StructuredOutputRepairingLlmClient(client, repairer);
    }

    private sealed class StructuredOutputRepairingLlmClient(
        ILlmClient inner,
        ILlmStructuredOutputRepairer repairer)
        : ILlmClient, ILlmCompletionClient, ILlmClientMetadataProvider
    {
        public LlmEndpointCapabilities Capabilities => inner.Capabilities;

        public LlmClientMetadata Metadata =>
            (inner as ILlmClientMetadataProvider)?.Metadata ??
            new LlmClientMetadata("Unknown", "Unknown");

        public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
            LlmRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (request.ResponseFormat?.Schema is null)
            {
                await foreach (var item in inner.StreamAsync(request, cancellationToken))
                    yield return item;
                yield break;
            }

            var events = new List<LlmStreamEvent>();
            var content = new StringBuilder();
            await foreach (var item in inner.StreamAsync(request, cancellationToken))
            {
                events.Add(item);
                if (item.Delta is not null)
                    content.Append(item.Delta);
            }

            var repaired = await repairer.RepairAsync(
                new LlmResponse(content.ToString()),
                request.ResponseFormat,
                cancellationToken);

            if (!repaired.ContentWasRepaired &&
                repaired.ContentRepairAttempts is null)
            {
                foreach (var item in events)
                    yield return item;
                yield break;
            }

            var emittedContent = false;
            foreach (var item in events)
            {
                if (item.Delta is not null)
                {
                    if (emittedContent)
                        continue;

                    emittedContent = true;
                    yield return item with
                    {
                        Delta = repaired.Content,
                        ContentWasRepaired = repaired.ContentWasRepaired,
                        ContentRepairAttempts = repaired.ContentRepairAttempts,
                        ContentRepairDiagnostics = repaired.ContentRepairDiagnostics
                    };
                    continue;
                }

                yield return item;
            }

            if (!emittedContent)
            {
                yield return new LlmStreamEvent
                {
                    ContentWasRepaired = repaired.ContentWasRepaired,
                    ContentRepairAttempts = repaired.ContentRepairAttempts,
                    ContentRepairDiagnostics = repaired.ContentRepairDiagnostics
                };
            }
        }

        public async Task<LlmResponse> CompleteAsync(
            LlmRequest request,
            CancellationToken cancellationToken = default)
        {
            var response = await inner.CompleteAsync(request, cancellationToken);
            return request.ResponseFormat?.Schema is null
                ? response
                : await repairer.RepairAsync(
                    response,
                    request.ResponseFormat,
                    cancellationToken);
        }
    }
}
