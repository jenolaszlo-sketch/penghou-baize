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
    public void Build_StructuredOutputWithTools_Throws()
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

        var action = () => builder.Build(ModelStrategy.StructuredOutput);

        action.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*mutually exclusive*");
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
    public void Build_NonStructuredOutputWithResponseFormat_Throws()
    {
        var action = () => new LlmPromptBuilder
        {
            Messages = TestMessage,
            ResponseFormat = LlmResponseFormat.JsonSchema("""{"type":"object"}""")
        }.Build(ModelStrategy.ToolCall);

        action.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*only valid for*");
    }
}