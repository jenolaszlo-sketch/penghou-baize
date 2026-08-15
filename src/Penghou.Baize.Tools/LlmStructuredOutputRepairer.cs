using Penghou.Nuwa;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Penghou.Baize.Tools;

/// <summary>
/// Default <see cref="ILlmStructuredOutputRepairer"/> implementation. Runs the
/// model's content through the JSON repair pipeline against the request's
/// schema, recording each repair strategy's disposition as an
/// <see cref="LlmRepairAttempt"/>.
/// </summary>
public sealed class LlmStructuredOutputRepairer(
    IJsonRepairPipeline jsonRepairPipeline,
    ILogger<LlmStructuredOutputRepairer>? logger = null)
    : ILlmStructuredOutputRepairer
{
    private readonly ILogger<LlmStructuredOutputRepairer> _logger =
        logger ?? NullLogger<LlmStructuredOutputRepairer>.Instance;

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

        var started = Stopwatch.GetTimestamp();
        using var activity = BaizeTelemetry.Activities.StartActivity(
            "llm.structured_output.repair",
            ActivityKind.Internal);
        activity?.SetTag("gen_ai.operation.name", "structured_output_repair");
        ToolsTelemetry.RepairAttempts.Add(1);

        try
        {
            using var repairResult =
                await jsonRepairPipeline.RepairAsync(
                    response.Content,
                    expectation,
                    cancellationToken);
            var attempts = PrefixAttempts(
                RepairAttemptMapper.Combine(repairResult),
                "content");
            var diagnostics = RepairAttemptMapper.ToDiagnostics(repairResult);

            activity?.SetTag("baize.repair.succeeded", repairResult.Document is not null);
            activity?.SetTag("baize.repair.changed", repairResult.WasRepaired);

            if (repairResult.Document is null ||
                repairResult.ShapeStatus == JsonRepairShapeStatus.Mismatched)
            {
                if (response.FinishReasonKind ==
                    LlmFinishReasonKind.LengthLimit)
                {
                    _logger.LogWarning(
                        "The model response reached its output token limit and remained invalid after deterministic JSON repair. Finish reason: {FinishReason}. Shape errors: {ShapeErrors}",
                        response.FinishReason,
                        diagnostics.ShapeErrors);
                }

                activity?.SetStatus(ActivityStatusCode.Ok);
                return response with
                {
                    ContentRepairAttempts = attempts,
                    ContentRepairDiagnostics = diagnostics
                };
            }

            if (repairResult.WasRepaired)
            {
                ToolsTelemetry.Repairs.Add(1);
                _logger.LogWarning(
                    response.FinishReasonKind ==
                        LlmFinishReasonKind.LengthLimit
                        ? "The model response reached its output token limit and would have failed structured-output parsing without deterministic JSON repair. Repair succeeded using {RepairStrategy}; finish reason: {FinishReason}"
                        : "The model returned structured output that would have failed without deterministic JSON repair. Repair succeeded using {RepairStrategy}; finish reason: {FinishReason}",
                    diagnostics.SucceededBy,
                    response.FinishReason);
            }
            activity?.SetStatus(ActivityStatusCode.Ok);
            return response with
            {
                Content = repairResult.Document.RootElement.GetRawText(),
                ContentWasRepaired = repairResult.WasRepaired,
                ContentRepairAttempts = attempts,
                ContentRepairDiagnostics = diagnostics
            };
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.SetTag("error.type", exception.GetType().FullName);
            throw;
        }
        finally
        {
            ToolsTelemetry.RepairDuration.Record(
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
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
