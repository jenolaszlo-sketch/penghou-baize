namespace Penghou.Baize.Diagnostics;

/// <summary>
/// Explicitly compares a client's native completion path with its reconstructed
/// streaming path. Intended for deterministic fixtures and provider debugging.
/// </summary>
public static class LlmStreamParityComparer
{
    /// <summary>
    /// Runs the request once through each path and compares response content
    /// exactly, without trimming or normalization.
    /// </summary>
    /// <remarks>
    /// This performs two provider requests and can therefore incur duplicate
    /// cost. Use only with deterministic test fixtures or when explicitly
    /// debugging an endpoint.
    /// </remarks>
    public static async Task<LlmStreamParityResult> CompareAsync(
        ILlmClient client,
        LlmRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        if (client is not ILlmCompletionClient completionClient)
        {
            throw new ArgumentException(
                "The client must implement ILlmCompletionClient to compare " +
                "native and streaming response paths.",
                nameof(client));
        }

        var streamed = await client.StreamAsync(request, cancellationToken)
            .CollectAsync(cancellationToken: cancellationToken);
        var nonStreaming = await completionClient.CompleteAsync(
            request,
            cancellationToken);
        var divergence = FindFirstDivergence(
            streamed.Content,
            nonStreaming.Content);

        return new LlmStreamParityResult(
            divergence is null,
            streamed.Content.Length,
            nonStreaming.Content.Length,
            divergence);
    }

    private static int? FindFirstDivergence(string streamed, string nonStreaming)
    {
        var commonLength = Math.Min(streamed.Length, nonStreaming.Length);
        for (var index = 0; index < commonLength; index++)
        {
            if (streamed[index] != nonStreaming[index])
                return index;
        }

        return streamed.Length == nonStreaming.Length
            ? null
            : commonLength;
    }
}

/// <summary>
/// Privacy-safe exact content comparison counts. Response content is not
/// retained in the result.
/// </summary>
/// <param name="IsExactMatch">Whether both paths returned exactly the same UTF-16 sequence.</param>
/// <param name="StreamedCharacterCount">The reconstructed stream length in UTF-16 code units.</param>
/// <param name="NonStreamingCharacterCount">The native response length in UTF-16 code units.</param>
/// <param name="FirstDivergenceIndex">
/// The first differing UTF-16 index, or the shorter length when one response
/// is an exact prefix of the other; null when both responses match.
/// </param>
public sealed record LlmStreamParityResult(
    bool IsExactMatch,
    int StreamedCharacterCount,
    int NonStreamingCharacterCount,
    int? FirstDivergenceIndex);
