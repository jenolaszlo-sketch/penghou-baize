using Penghou.Nuwa;
using Penghou.Baize;

namespace Penghou.Baize.Tools;

/// <summary>
/// Default <see cref="ILlmResponseNormalizer"/> implementation. Repairs the
/// arguments of existing native tool calls against each tool's JSON Schema;
/// when the response carries no usable native calls, it falls back to
/// <see cref="IContentToolCallExtractor"/> to recover calls from plain-text
/// content. Responses with no declared tools are returned unchanged.
/// </summary>
/// <param name="contentToolCallExtractor">Extracts tool calls embedded in model content.</param>
/// <param name="jsonRepairPipeline">Repairs malformed tool-call JSON.</param>
public sealed class LlmResponseNormalizer(
    IContentToolCallExtractor contentToolCallExtractor,
    IJsonRepairPipeline jsonRepairPipeline)
    : ILlmResponseNormalizer
{
    /// <inheritdoc />
    public async Task<LlmResponse> NormalizeAsync(
        LlmResponse response,
        IReadOnlyCollection<LlmTool> tools,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(tools);

        if (tools.Count == 0)
            return response;

        var toolsByName = tools
            .GroupBy(
                tool => tool.Name,
                StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.Ordinal);
        var knownToolNameSet = new HashSet<string>(
            toolsByName.Keys,
            StringComparer.Ordinal);

        var existingToolCalls = response.ToolCalls ?? [];

        if (existingToolCalls.Count > 0)
        {
            // Preserve the complete native-call collection, canonicalizing the
            // calls that name a declared tool and carry arguments. Unknown
            // calls and calls without arguments are kept with an explicit
            // status rather than silently dropped, so the application can
            // audit or reject them.
            var normalized = new List<LlmToolCall>(existingToolCalls.Count);

            foreach (var toolCall in existingToolCalls)
            {
                var isKnown =
                    !string.IsNullOrWhiteSpace(toolCall.Name) &&
                    knownToolNameSet.Contains(toolCall.Name);
                var hasArguments =
                    !string.IsNullOrWhiteSpace(toolCall.ArgumentsJson);

                if (isKnown && hasArguments)
                {
                    normalized.Add(await CanonicalizeArguments(
                        toolCall,
                        GetExpectation(
                            toolCall.Name,
                            toolsByName),
                        cancellationToken));
                }
                else if (isKnown)
                {
                    normalized.Add(toolCall with
                    {
                        NormalizationStatus =
                            LlmToolCallNormalizationStatus.EmptyArguments
                    });
                }
                else
                {
                    normalized.Add(toolCall with
                    {
                        NormalizationStatus =
                            LlmToolCallNormalizationStatus.UnknownTool
                    });
                }
            }

            return response with
            {
                ToolCalls = normalized
            };
        }

        var syntheticToolCalls = await contentToolCallExtractor.ExtractAsync(
            response.Content,
            tools,
            cancellationToken);

        if (syntheticToolCalls.Count == 0)
            return response;

        return response with
        {
            ToolCalls = syntheticToolCalls
        };
    }

    private async Task<LlmToolCall> CanonicalizeArguments(
        LlmToolCall toolCall,
        JsonSchemaExpectation? expectation,
        CancellationToken cancellationToken)
    {
        using var repairResult =
            await jsonRepairPipeline.RepairAsync(
                toolCall.ArgumentsJson,
                expectation,
                cancellationToken);
        var currentAttempts = PrefixAttempts(
            RepairAttemptMapper.Combine(repairResult),
            "arguments");
        var currentDiagnostics = RepairAttemptMapper.ToDiagnostics(repairResult);
        var attempts = MergeAttempts(
            toolCall.JsonRepairAttempts,
            currentAttempts,
            appendCurrent:
                repairResult.WasRepaired ||
                !repairResult.IsRepairAccepted);
        var diagnostics =
            toolCall.JsonRepairDiagnostics?.IsRepairAccepted == true &&
            repairResult.IsRepairAccepted &&
            !repairResult.WasRepaired
                ? toolCall.JsonRepairDiagnostics
                : currentDiagnostics;

        if (!repairResult.IsRepairAccepted)
        {
            return toolCall with
            {
                JsonRepairAttempts =
                    attempts,
                JsonRepairDiagnostics = diagnostics,
                NormalizationStatus =
                    LlmToolCallNormalizationStatus.InvalidArguments
            };
        }

        var repairedDocument = repairResult.Document!;
        return toolCall with
        {
            ArgumentsJson =
                repairedDocument.RootElement.GetRawText(),
            JsonWasRepaired =
                toolCall.JsonWasRepaired ||
                repairResult.WasRepaired,
            JsonRepairAttempts =
                attempts,
            JsonRepairDiagnostics = diagnostics,
            NormalizationStatus =
                LlmToolCallNormalizationStatus.Normalized
        };
    }

    private static JsonSchemaExpectation? GetExpectation(
        string toolName,
        IReadOnlyDictionary<string, LlmTool> toolsByName) =>
        toolsByName.TryGetValue(
            toolName,
            out var tool)
            ? JsonSchemaExpectation.FromSchemaJson(
                tool.InputSchemaJson)
            : null;

    private static IReadOnlyList<LlmRepairAttempt>
        PrefixAttempts(
            IReadOnlyList<LlmRepairAttempt> attempts,
            string scope) =>
        attempts
            .Select(attempt =>
                attempt with
                {
                    Name = $"{scope}/{attempt.Name}"
                })
            .ToArray();

    private static IReadOnlyList<LlmRepairAttempt> MergeAttempts(
        IReadOnlyList<LlmRepairAttempt>? existing,
        IReadOnlyList<LlmRepairAttempt> current,
        bool appendCurrent)
    {
        if (existing is null || existing.Count == 0)
            return current;
        if (!appendCurrent)
            return existing;

        return existing.Concat(current).ToArray();
    }
}
