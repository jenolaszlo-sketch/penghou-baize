using Penghou.Baize.Qualification.Execution;
using Penghou.Baize.Qualification.Planning;

namespace Penghou.Baize.Qualification.Tests;

public sealed class DeploymentClosureTests
{
    [Fact]
    public void BuildIdentityRecordsProductRevisionAndConfiguration()
    {
        var build = BuildBinding.Collect();
        Assert.Equal("Release", build.GetProperty("configuration").GetString());
        Assert.False(string.IsNullOrWhiteSpace(build.GetProperty("buildRevision").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(build.GetProperty("productVersion").GetString()));
        Assert.Equal("baize-deployment-closure@1",
            build.GetProperty("deploymentClosure").GetProperty("schemaVersion").GetString());
    }

    [Fact]
    public void DeploymentClosure_IsStableSortedAndRejectsChangedRuntimeFile()
    {
        using var workspace = new Stage3Workspace();
        var root = workspace.Out("deployment-closure");
        Directory.CreateDirectory(root);
        var runnerPath = Path.Combine(root, "Penghou.Baize.Qualification.dll");
        File.Copy(typeof(BuildBinding).Assembly.Location, runnerPath);
        File.WriteAllText(Path.Combine(root, "z.runtimeconfig.json"), "{}", new System.Text.UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "a.deps.json"), "{}", new System.Text.UTF8Encoding(false));

        var closure = BuildBinding.CollectDeploymentClosureAt(root, runnerPath);
        var build = CanonicalJson.Element(new { deploymentClosure = closure });
        var files = closure.GetProperty("files").EnumerateArray()
            .Select(file => file.GetProperty("path").GetString()!).ToArray();

        Assert.Equal(files.Order(StringComparer.Ordinal), files);
        Assert.Equal("Penghou.Baize.Qualification.dll", closure.GetProperty("runnerPath").GetString());
        Assert.Equal("baize-deployment-closure@1", closure.GetProperty("schemaVersion").GetString());
        BuildBinding.VerifyDeploymentClosureAt(build, root, runnerPath);

        File.AppendAllText(Path.Combine(root, "z.runtimeconfig.json"), "changed");
        Assert.Equal("deployment-closure-changed", Assert.Throws<ExecutionException>(
            () => BuildBinding.VerifyDeploymentClosureAt(build, root, runnerPath)).Code);
    }
}
