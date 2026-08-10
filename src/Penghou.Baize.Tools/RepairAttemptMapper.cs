using Penghou.Nuwa;

namespace Penghou.Baize.Tools;

/// <summary>
/// Maps Nuwa repair diagnostics onto the core <see cref="LlmRepairAttempt"/>
/// shape so that Baize core stays independent of Nuwa.
/// </summary>
internal static class RepairAttemptMapper
{
    public static IReadOnlyList<LlmRepairAttempt> Combine(
        JsonRepairResult result) =>
        result.TextRepairs
            .Concat(result.NodeRepairs)
            .Select(ToAttempt)
            .ToArray();

    public static LlmJsonRepairDiagnostics ToDiagnostics(
        JsonRepairResult result) =>
        new(
            MapShapeStatus(result.ShapeStatus),
            result.ShapeErrors.ToArray(),
            result.SucceededBy?.Name,
            result.TolerantRecovery is null
                ? null
                : new LlmTolerantRecoveryDiagnostics(
                    result.TolerantRecovery.Succeeded,
                    result.TolerantRecovery.Outcome,
                    result.TolerantRecovery.CorrectionCount,
                    result.TolerantRecovery.SchemaGuidedStringCorrectionCount,
                    result.TolerantRecovery.Corrections.ToArray()));

    private static LlmRepairAttempt ToAttempt(
        StrategyReport report) =>
        new(
            report.Name,
            MapStatus(report.Status),
            report.Repaired,
            report.Note);

    private static LlmRepairStatus MapStatus(
        StrategyStatus status) =>
        status switch
        {
            StrategyStatus.Skipped => LlmRepairStatus.Skipped,
            StrategyStatus.NotApplicable => LlmRepairStatus.NotApplicable,
            StrategyStatus.Failed => LlmRepairStatus.Failed,
            StrategyStatus.Succeeded => LlmRepairStatus.Succeeded,
            _ => throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                null)
        };

    private static LlmRepairShapeStatus MapShapeStatus(
        JsonRepairShapeStatus status) =>
        status switch
        {
            JsonRepairShapeStatus.NotEvaluated =>
                LlmRepairShapeStatus.NotEvaluated,
            JsonRepairShapeStatus.Matched =>
                LlmRepairShapeStatus.Matched,
            JsonRepairShapeStatus.Mismatched =>
                LlmRepairShapeStatus.Mismatched,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };
}
