using FluentAssertions;

namespace Penghou.Baize.Tests;

public sealed class LlmPromptBuilderTests
{
    private static readonly LlmMessage[] TestMessage =
        [new LlmMessage("user", "Hi")];

    [Fact]
    public void Build_AutoStrategy_KeepsConfiguredTools()
    {
        var tool = new LlmTool("get_weather", "Weather lookup", "{}");

        var request = new LlmPromptBuilder
        {
            Messages = TestMessage,
            Tools = [tool]
        }.Build(ModelStrategy.Auto);

        request.Tools.Should().ContainSingle().Which.Should().Be(tool);
    }

    [Fact]
    public void Build_PreservesToolsAndStructuredResponseFormat()
    {
        var builder = new LlmPromptBuilder
        {
            Messages = TestMessage,
            Tools =
            [
                new LlmTool("emit_files", "Emits files", """{"type":"object"}""")
            ],
            ResponseFormat = LlmResponseFormat.JsonSchema("""{"type":"object"}""")
        };

        var request = builder.Build(ModelStrategy.StructuredOutput);

        request.Tools.Should().ContainSingle();
        request.ResponseFormat.Should().NotBeNull();
    }

    [Fact]
    public void Build_StructuredOutputWithoutTools_Succeeds()
    {
        var request = new LlmPromptBuilder
        {
            Messages = TestMessage,
            ResponseFormat = LlmResponseFormat.JsonSchema("""{"type":"object"}""")
        }.Build(ModelStrategy.StructuredOutput);

        request.ResponseFormat.Should().NotBeNull();
        request.Tools.Should().BeEmpty();
    }

    [Fact]
    public void Build_PreservesResponseFormatForAnyRoutingStrategy()
    {
        var request = new LlmPromptBuilder
        {
            Messages = TestMessage,
            ResponseFormat = LlmResponseFormat.JsonSchema("""{"type":"object"}""")
        }.Build(ModelStrategy.ToolCall);

        request.ResponseFormat.Should().NotBeNull();
    }

    [Fact]
    public void Build_PreservesRequestMetadataAsASnapshot()
    {
        var metadata = new Dictionary<string, object?>
        {
            ["acme.tenant-id"] = "tenant-a",
            ["acme.low-cost"] = true
        };
        var builder = new LlmPromptBuilder
        {
            Messages = TestMessage,
            Metadata = metadata
        };

        var request = builder.Build(ModelStrategy.Auto);
        metadata.Clear();

        request.Metadata.Should().HaveCount(2);
        request.Metadata["acme.tenant-id"].Should().Be("tenant-a");
        request.Metadata["acme.low-cost"].Should().Be(true);
    }
}
