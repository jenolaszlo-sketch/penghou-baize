using Penghou.Nuwa;

namespace Penghou.Baize.Tools;

/// <summary>
/// Default <see cref="ILlmStructuredOutputRepairer"/> implementation. Runs the
/// model's content through the JSON repair pipeline against the request's
/// schema, recording each repair strategy's disposition as an
/// <see cref="LlmRepairAttempt"/>.
/// </summary>
public sealed class LlmStructuredOutputRepairer(
    IJsonRepairPipeline jsonRepairPipeline)
    : ILlmStructuredOutputRepairer
{
    /// <inheritdoc />
    public async Task<LlmResponse> RepairAsync(
        LlmResponse response,
        LlmResponseFormat responseFormat,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(responseFormat);

        if (string.IsNullOrWhiteSpace(response.Content))
            return response;

        var expectation =
            JsonSchemaExpectation.FromSchemaJson(
                responseFormat.Schema);

        if (expectation is null)
            return response;

        using var repairResult =
            await jsonRepairPipeline.RepairAsync(
                response.Content,
                expectation,
                cancellationToken);
        var attempts = PrefixAttempts(
            RepairAttemptMapper.Combine(repairResult),
            "content");
        var diagnostics = RepairAttemptMapper.ToDiagnostics(repairResult);

        if (repairResult.Document is null ||
            repairResult.ShapeStatus == JsonRepairShapeStatus.Mismatched)
        {
            return response with
            {
                ContentRepairAttempts = attempts,
                ContentRepairDiagnostics = diagnostics
            };
        }

        return response with
        {
            Content =
                repairResult.Document.RootElement.GetRawText(),
            ContentWasRepaired =
                repairResult.WasRepaired,
            ContentRepairAttempts = attempts,
            ContentRepairDiagnostics = diagnostics
        };
    }

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
}
