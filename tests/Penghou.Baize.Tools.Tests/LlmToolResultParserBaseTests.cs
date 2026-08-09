using FluentAssertions;
using Penghou.Nuwa;
using Penghou.Baize;
using Penghou.Baize.Tools;
using Penghou.Baize.Tools.Schema;
using System.Text.Json.Serialization;

namespace Penghou.Baize.Tools.Repair.Tests;

public sealed class LlmToolResultParserBaseTests
{
    [Fact]
    public void Parse_ReturnsValue_WhenArgumentsMatchSchema()
    {
        var parser = CreateParser();
        var response = CreateResponse(
            """{"name":"Ada","items":[{"count":2}]}""");

        var result = parser.Parse(response);

        result.Succeeded.Should().BeTrue();
        result.Failure.Should().Be(ToolCallParseFailure.None);
        result.Value.Should().NotBeNull();
        result.Value!.Name.Should().Be("Ada");
        result.Value.Items.Should().ContainSingle()
            .Which.Count.Should().Be(2);
    }

    [Fact]
    public void Parse_Fails_WhenArgumentsAreMalformedJson()
    {
        var parser = CreateParser();
        var response = CreateResponse(
            """{"name":"Ada","items":[{"count":2}]""");

        var result = parser.Parse(response);

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().Be(ToolCallParseFailure.InvalidJson);
        result.Error.Should().StartWith("Invalid JSON:");
        result.Raw.Should().Be(
            """{"name":"Ada","items":[{"count":2}]""");
    }

    [Fact]
    public void Parse_Fails_WhenRequiredPropertyIsMissing()
    {
        var parser = CreateParser();
        var response = CreateResponse(
            """{"items":[{"count":2}]}""");

        var result = parser.Parse(response);

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().Be(ToolCallParseFailure.SchemaValidationFailed);
        result.Error.Should().Contain("$.name is required");
        result.Raw.Should().Be("""{"items":[{"count":2}]}""");
    }

    [Fact]
    public void Parse_Fails_WhenNestedPropertyHasWrongType()
    {
        var parser = CreateParser();
        var response = CreateResponse(
            """{"name":"Ada","items":[{"count":"two"}]}""");

        var result = parser.Parse(response);

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().Be(ToolCallParseFailure.SchemaValidationFailed);
        result.Error.Should().Contain("$.items[0].count expected");
    }

    [Fact]
    public void Parse_FailsWithoutThrowing_WhenObjectHasDuplicateProperty()
    {
        var parser = CreateParser();
        var response = CreateResponse(
            """
            {
              "name": "Ada",
              "items": [
                {
                  "count": 1,
                  "count": 2
                }
              ]
            }
            """);

        var result = parser.Parse(response);

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().Be(
            ToolCallParseFailure.SchemaValidationFailed);
        result.Error.Should().Contain(
            "duplicate property '$.items[0].count'");
    }

    [Fact]
    public void Parse_FailsWithTypedReason_WhenToolCallIsMissing()
    {
        var parser = CreateParser();
        var response = new LlmResponse(Content: "No tool call");

        var result = parser.Parse(response);

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().Be(ToolCallParseFailure.MissingToolCall);
    }

    [Fact]
    public void Parse_FailsWithTypedReason_WhenArgumentsAreEmpty()
    {
        var parser = CreateParser();
        var response = CreateResponse(string.Empty);

        var result = parser.Parse(response);

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().Be(ToolCallParseFailure.EmptyArguments);
    }

    private static TestParser CreateParser()
    {
        return new TestParser();
    }

    private static LlmResponse CreateResponse(string argumentsJson) =>
        new(
            Content: string.Empty,
            ToolCalls:
            [
                new LlmToolCall(
                    Id: "call-1",
                    Name: "test_tool",
                    ArgumentsJson: argumentsJson)
            ]);

    private sealed class TestParser()
        : LlmToolResultParserBase<TestArguments>(
            "test_tool",
            JsonSchemaExpectation.FromSchemaNode(
                JsonSchemaGenerator.GenerateSchemaNode<TestArguments>()));

    private sealed class TestArguments
    {
        [JsonPropertyName("name")]
        public required string Name { get; init; }

        [JsonPropertyName("items")]
        public required List<TestItem> Items { get; init; }
    }

    private sealed class TestItem
    {
        [JsonPropertyName("count")]
        public required int Count { get; init; }
    }
}
