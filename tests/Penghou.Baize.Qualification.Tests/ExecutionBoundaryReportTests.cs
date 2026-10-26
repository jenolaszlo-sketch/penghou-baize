using System.Text.Json;
using Penghou.Baize.Qualification.Execution;
using Penghou.Baize.Qualification.Planning;

namespace Penghou.Baize.Qualification.Tests;

public sealed class ExecutionBoundaryReportTests
{
    [Fact]
    public async Task ReportCountsUniqueClientDispatchesAndSeparatesProviderExecution()
    {
        using var workspace = new Stage3Workspace();
        (await workspace.PlanAsync("fake-alpha", "chat.baseline@3")).AssertCompleted("plan-generated", 0);
        (await workspace.RunAsync("fake-alpha", "chat.baseline@3")).AssertCompleted("run-summary", 0);
        (await workspace.ReportAsync()).AssertCompleted("report-summary", 0);

        using var reportFile = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(workspace.RunDir,
            "reports", "summary.json")));
        var report = reportFile.RootElement.GetProperty("report");
        var boundary = report.GetProperty("executionBoundary");

        Assert.Equal(CanonicalJson.EvidenceDigestVersion,
            report.GetProperty("evidenceCanonicalization").GetString());
        Assert.Equal(0, boundary.GetProperty("authorizationClaimCount").GetInt32());
        Assert.Equal(1, boundary.GetProperty("baizeDispatchCount").GetInt64());
        Assert.Equal(1, boundary.GetProperty("clientTransportAttemptCount").GetInt64());
        Assert.True(boundary.GetProperty("clientTransportAttemptCountComplete").GetBoolean());
        Assert.Equal(1, boundary.GetProperty("observedHttpResponseCount").GetInt64());
        Assert.Equal(JsonValueKind.Null, boundary.GetProperty("providerReportedModelOrRoute").ValueKind);
        Assert.Equal("unknown", boundary.GetProperty("providerExecutionCount").GetString());
        Assert.Equal(1, report.GetProperty("cases")[0].GetProperty("attempts")[0]
            .GetProperty("httpDispatches").GetInt32());
        Assert.Contains("not an exactly-once provider-processing guarantee",
            File.ReadAllText(Path.Combine(workspace.RunDir, "reports", "report.md")));
    }

    [Fact]
    public async Task CombinedBaselineAndStreamingObservation_UsesOneSyntheticDispatch()
    {
        using var workspace = new Stage3Workspace();
        const string contracts = "chat.baseline@3,chat.streaming-integrity@2";
        (await workspace.PlanAsync("fake-alpha", contracts)).AssertCompleted("plan-generated", 0);
        (await workspace.RunAsync("fake-alpha", contracts)).AssertCompleted("run-summary", 0);
        (await workspace.ReportAsync()).AssertCompleted("report-summary", 0);

        using var reportFile = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(workspace.RunDir,
            "reports", "summary.json")));
        var report = reportFile.RootElement.GetProperty("report");
        var boundary = report.GetProperty("executionBoundary");
        var reportCase = Assert.Single(report.GetProperty("cases").EnumerateArray());

        Assert.Equal(1, boundary.GetProperty("clientTransportAttemptCount").GetInt64());
        Assert.Equal(1, report.GetProperty("consumedModelCalls").GetInt64());
        Assert.Equal(contracts.Split(',').Order(StringComparer.Ordinal),
            reportCase.GetProperty("observedContractIds").EnumerateArray()
                .Select(value => value.GetString()!).Order(StringComparer.Ordinal));
    }
}
