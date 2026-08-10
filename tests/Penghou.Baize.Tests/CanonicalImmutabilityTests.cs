using FluentAssertions;

namespace Penghou.Baize.Tests;

public sealed class CanonicalImmutabilityTests
{
    [Fact]
    public void Request_TakesSnapshotsOfMessagesAndTools()
    {
        var messages = new List<LlmMessage> { new("user", "hello") };
        var tools = new List<LlmTool>
        {
            new("emit", "Emit output", "{\"type\":\"object\"}")
        };

        var request = new LlmRequest(messages, tools: tools);

        messages.Clear();
        tools.Clear();

        request.Messages.Should().HaveCount(1);
        request.Tools.Should().HaveCount(1);
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
