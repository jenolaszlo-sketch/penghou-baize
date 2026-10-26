using System.Text;
using System.Text.Json;
using Penghou.Baize.Qualification.Execution;
using Penghou.Baize.Qualification.Planning;

namespace Penghou.Baize.Qualification.Tests;

public sealed class CanonicalEvidenceTests
{
    [Fact]
    public void CanonicalEvidenceVectors_AreStableAcrossPropertyOrderWhitespaceAndLineEndings()
    {
        using var stream = typeof(CanonicalEvidenceTests).Assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith("Stage4bFixtures.canonical-evidence-vectors.json", StringComparison.Ordinal))
            .Select(name => typeof(CanonicalEvidenceTests).Assembly.GetManifestResourceStream(name))
            .Single(value => value is not null)!;
        using var vectors = JsonDocument.Parse(stream);
        foreach (var vector in vectors.RootElement.GetProperty("vectors").EnumerateArray())
        {
            var expectedCanonical = vector.GetProperty("canonicalUtf8").GetString();
            var expectedDigest = vector.GetProperty("sha256").GetString();
            foreach (var input in vector.GetProperty("inputs").EnumerateArray())
            {
                var json = input.GetString()!;
                Assert.Equal(expectedDigest, CanonicalJson.EvidenceIdentity(json));
                Assert.Equal(expectedCanonical, Encoding.UTF8.GetString(CanonicalJson.CanonicalizeEvidence(json)));
            }
        }
    }

    [Fact]
    public void CanonicalEvidenceRejectsInvalidJsonAndDuplicateKeys()
    {
        Assert.ThrowsAny<JsonException>(() => CanonicalJson.EvidenceIdentity("{not-json}"));
        var duplicate = Assert.Throws<PlanningException>(() =>
            CanonicalJson.EvidenceIdentity("{\"authorizationRef\":\"a\",\"AuthorizationRef\":\"b\"}"));
        Assert.Equal("json-duplicate-property", duplicate.Code);
    }

    [Fact]
    public void EvidenceJsonFileIsValidUtf8WithoutBomAndDeterministicallyCanonical()
    {
        using var workspace = new Stage3Workspace();
        var path = workspace.Out("validate-live-output.json");
        EvidenceJsonFile.WriteCanonical(path, "{\r\n  \"z\": 2,\r\n  \"a\": 1\r\n}\r\n");
        var bytes = File.ReadAllBytes(path);
        Assert.False(bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
        Assert.Equal("{\"a\":1,\"z\":2}", Encoding.UTF8.GetString(bytes));
        using var parsed = JsonDocument.Parse(bytes);
        Assert.Equal(1, parsed.RootElement.GetProperty("a").GetInt32());
        Assert.Equal(2, parsed.RootElement.GetProperty("z").GetInt32());
    }
}
