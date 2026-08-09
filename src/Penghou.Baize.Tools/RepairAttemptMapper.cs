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
}
