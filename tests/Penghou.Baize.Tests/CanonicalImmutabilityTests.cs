using FluentAssertions;

namespace Penghou.Baize.Tests;

public sealed class CanonicalImmutabilityTests
{
    [Fact]
    public void Request_TakesSnapshotsOfMessagesToolsAndMetadata()
    {
        var messages = new List<LlmMessage> { new("user", "hello") };
        var tools = new List<LlmTool>
        {
            new("emit", "Emit output", "{\"type\":\"object\"}")
        };
        var metadata = new Dictionary<string, object?>
        {
            ["acme.tenant-id"] = "tenant-a"
        };

        var request = new LlmRequest(messages, tools: tools, metadata: metadata);

        messages.Clear();
        tools.Clear();
        metadata["acme.tenant-id"] = "tenant-b";
        metadata["new"] = true;

        request.Messages.Should().HaveCount(1);
        request.Tools.Should().HaveCount(1);
        request.Metadata.Should().ContainSingle()
            .Which.Should().Be(new KeyValuePair<string, object?>(
                "acme.tenant-id",
                "tenant-a"));
    }

    [Fact]
    public void Message_TakesSnapshotOfParts()
    {
        var parts = new List<LlmContentPart> { new LlmTextContent("hello") };
        var message = new LlmMessage("user", parts);

        parts.Clear();

        message.Parts.Should().ContainSingle();
    }
}
