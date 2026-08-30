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
    public void Parse_ReportsTruncation_WhenMalformedJsonReachedLengthLimit()
    {
        var parser = CreateParser();
        var toolCall = new LlmToolCall(
            "call-1",
            "test_tool",
            "{\"name\":\"Ada",
            JsonRepairAttempts:
            [
                new LlmRepairAttempt(
                    "arguments/tolerant-syntax-tree",
                    LlmRepairStatus.Succeeded)
            ])
        {
            JsonRepairDiagnostics = new LlmJsonRepairDiagnostics(
                LlmRepairShapeStatus.Mismatched,
                ["$.items is required."])
        };
        var response = new LlmResponse(
            string.Empty,
            FinishReason: "length",
            ToolCalls: [toolCall]);

        var result = parser.Parse(response);

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().Be(
            ToolCallParseFailure.TruncatedResponse);
        result.Error.Should().Contain("output token limit");
        result.Error.Should().Contain("repair was attempted");
        result.Error.Should().Contain("$.items is required");
        result.Error.Should().Contain("Invalid JSON:");
    }

    [Fact]
    public void Parse_ReportsTruncation_WhenSchemaIsIncompleteAtLengthLimit()
    {
        var parser = CreateParser();
        var response = CreateResponse(
            """{"items":[{"count":2}]}""",
            finishReason: "max_tokens");

        var result = parser.Parse(response);

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().Be(
            ToolCallParseFailure.TruncatedResponse);
        result.Error.Should().Contain("$.name is required");
    }

    [Fact]
    public void Parse_Succeeds_WhenLengthLimitedJsonIsStillComplete()
    {
        var parser = CreateParser();
        var response = CreateResponse(
            """{"name":"Ada","items":[{"count":2}]}""",
            finishReason: "length");

        var result = parser.Parse(response);

        result.Succeeded.Should().BeTrue();
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

    [Fact]
    public void Parse_ReturnsTypedFailure_WhenClrMappingIsUnsupported()
    {
        var parser = new UnsupportedParser();
        var response = new LlmResponse(
            Content: string.Empty,
            ToolCalls:
            [
                new LlmToolCall(
                    Id: "call-1",
                    Name: "unsupported_tool",
                    ArgumentsJson: """{"value":"System.String"}""")
            ]);

        var result = parser.Parse(response);

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().Be(
            ToolCallParseFailure.DeserializationFailed);
        result.Error.Should().NotBeNullOrWhiteSpace();
        result.Raw.Should().Be("""{"value":"System.String"}""");
    }

    private static TestParser CreateParser()
    {
        return new TestParser();
    }

    private static LlmResponse CreateResponse(
        string argumentsJson,
        string? finishReason = null) =>
        new(
            Content: string.Empty,
            FinishReason: finishReason,
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

    private sealed class UnsupportedParser()
        : LlmToolResultParserBase<UnsupportedArguments>(
            "unsupported_tool",
            JsonSchemaExpectation.FromSchemaJson(
                """
                {
                  "type": "object",
                  "properties": {
                    "value": { "type": "string" }
                  },
                  "required": ["value"]
                }
                """)!);

    private sealed class UnsupportedArguments
    {
        [JsonPropertyName("value")]
        public required Type Value { get; init; }
    }
}
