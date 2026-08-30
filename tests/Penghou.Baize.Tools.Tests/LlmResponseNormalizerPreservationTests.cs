using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Penghou.Nuwa;
using Penghou.Nuwa.Strategies;
using Penghou.Baize;
using Penghou.Baize.Tools;

namespace Penghou.Baize.Tools.Repair.Tests;

public sealed class LlmResponseNormalizerPreservationTests
{
    [Fact]
    public async Task Normalize_PreservesUnknownToolCallWithStatus()
    {
        var pipeline = CreatePipeline();
        var normalizer = new LlmResponseNormalizer(
            new ContentToolCallExtractor(pipeline),
            pipeline);
        var response = new LlmResponse(
            Content: string.Empty,
            ToolCalls:
            [
                new LlmToolCall("call-1", "known_tool", """{"a":1}"""),
                new LlmToolCall("call-2", "undeclared_tool", """{"x":2}""")
            ]);

        var normalized = await normalizer.NormalizeAsync(
            response,
            [new LlmTool("known_tool", "Known", """{"type":"object"}""")],
            TestContext.Current.CancellationToken);

        normalized.ToolCalls.Should().HaveCount(2);
        normalized.ToolCalls![0].NormalizationStatus
            .Should().Be(LlmToolCallNormalizationStatus.Normalized);
        normalized.ToolCalls[0].ArgumentsJson.Should().Be("""{"a":1}""");
        normalized.ToolCalls[1].NormalizationStatus
            .Should().Be(LlmToolCallNormalizationStatus.UnknownTool);
        normalized.ToolCalls[1].ArgumentsJson.Should().Be("""{"x":2}""");
    }

    [Fact]
    public async Task Normalize_PreservesKnownCallWithEmptyArguments()
    {
        var pipeline = CreatePipeline();
        var normalizer = new LlmResponseNormalizer(
            new ContentToolCallExtractor(pipeline),
            pipeline);
        var response = new LlmResponse(
            Content: string.Empty,
            ToolCalls:
            [
                new LlmToolCall("call-1", "known_tool", """{"a":1}"""),
                new LlmToolCall("call-2", "known_tool", "")
            ]);

        var normalized = await normalizer.NormalizeAsync(
            response,
            [new LlmTool("known_tool", "Known", """{"type":"object"}""")],
            TestContext.Current.CancellationToken);

        normalized.ToolCalls.Should().HaveCount(2);
        normalized.ToolCalls![0].NormalizationStatus
            .Should().Be(LlmToolCallNormalizationStatus.Normalized);
        normalized.ToolCalls[1].NormalizationStatus
            .Should().Be(LlmToolCallNormalizationStatus.EmptyArguments);
    }

    [Fact]
    public async Task Normalize_KeepsRepairMetadataOnNormalizedCall()
    {
        var pipeline = CreatePipeline();
        var normalizer = new LlmResponseNormalizer(
            new ContentToolCallExtractor(pipeline),
            pipeline);
        var response = new LlmResponse(
            Content: string.Empty,
            ToolCalls:
            [
                new LlmToolCall(
                    "call-1",
                    "known_tool",
                    """{"files":[{"path":"A.cs","content":value}]}""")
            ]);

        var normalized = await normalizer.NormalizeAsync(
            response,
            [new LlmTool(
                "known_tool",
                "Known",
                """
                {
                  "type": "object",
                  "properties": {
                    "files": {
                      "type": "array",
                      "items": {
                        "type": "object",
                        "properties": {
                          "path": { "type": "string" },
                          "content": { "type": "string" }
                        },
                        "required": ["path", "content"]
                      }
                    }
                  },
                  "required": ["files"]
                }
                """)],
            TestContext.Current.CancellationToken);

        var call = normalized.ToolCalls.Should().ContainSingle().Subject;
        call.NormalizationStatus
            .Should().Be(LlmToolCallNormalizationStatus.Normalized);
        call.JsonWasRepaired.Should().BeTrue();
        call.JsonRepairAttempts.Should().NotBeNull();
    }

    [Fact]
    public async Task Normalize_PreservesRejectedArgumentsWithInvalidStatus()
    {
        var pipeline = CreatePipeline();
        var normalizer = new LlmResponseNormalizer(
            new ContentToolCallExtractor(pipeline),
            pipeline);
        const string argumentsJson = """{"unexpected":1}""";
        var response = new LlmResponse(
            Content: string.Empty,
            ToolCalls:
            [
                new LlmToolCall(
                    "call-1",
                    "known_tool",
                    argumentsJson)
            ]);

        var normalized = await normalizer.NormalizeAsync(
            response,
            [new LlmTool(
                "known_tool",
                "Known",
                """
                {
                  "type": "object",
                  "properties": {
                    "name": { "type": "string" }
                  },
                  "required": ["name"],
                  "additionalProperties": false
                }
                """)],
            TestContext.Current.CancellationToken);

        var call = normalized.ToolCalls.Should().ContainSingle().Subject;
        call.NormalizationStatus.Should().Be(
            LlmToolCallNormalizationStatus.InvalidArguments);
        call.ArgumentsJson.Should().Be(argumentsJson);
        call.JsonWasRepaired.Should().BeFalse();
        call.JsonRepairDiagnostics.Should().NotBeNull();
        call.JsonRepairDiagnostics!.IsRepairAccepted.Should().BeFalse();
        call.JsonRepairDiagnostics.ShapeErrors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Normalize_RepeatedlyPreservesExistingRepairProvenance()
    {
        var pipeline = CreatePipeline();
        var normalizer = new LlmResponseNormalizer(
            new ContentToolCallExtractor(pipeline),
            pipeline);
        var priorAttempt = new LlmRepairAttempt(
            "provider/prior-repair",
            LlmRepairStatus.Succeeded);
        var priorDiagnostics = new LlmJsonRepairDiagnostics(
            LlmRepairShapeStatus.Matched,
            [],
            SucceededBy: "prior-repair")
        {
            IsRepairAccepted = true
        };
        var call = new LlmToolCall(
            "call-1",
            "known_tool",
            """{"name":"Ada"}""",
            JsonWasRepaired: true,
            JsonRepairAttempts: [priorAttempt])
        {
            JsonRepairDiagnostics = priorDiagnostics
        };
        var response = new LlmResponse(
            Content: string.Empty,
            ToolCalls: [call]);
        var tools = new[]
        {
            new LlmTool(
                "known_tool",
                "Known",
                """
                {
                  "type": "object",
                  "properties": {
                    "name": { "type": "string" }
                  },
                  "required": ["name"]
                }
                """)
        };

        var first = await normalizer.NormalizeAsync(
            response,
            tools,
            TestContext.Current.CancellationToken);
        var second = await normalizer.NormalizeAsync(
            first,
            tools,
            TestContext.Current.CancellationToken);

        var normalizedCall = second.ToolCalls.Should().ContainSingle().Subject;
        normalizedCall.NormalizationStatus.Should().Be(
            LlmToolCallNormalizationStatus.Normalized);
        normalizedCall.JsonWasRepaired.Should().BeTrue();
        normalizedCall.JsonRepairAttempts.Should().Equal(priorAttempt);
        normalizedCall.JsonRepairDiagnostics.Should().Be(priorDiagnostics);
    }

    private static JsonRepairPipeline CreatePipeline() =>
        new(
            [],
            [],
            [
                new SchemaGuidedOptionalNullRemovalStrategy(),
                new SchemaGuidedJsonStringExpansionStrategy()
            ],
            NullLogger<JsonRepairPipeline>.Instance);
}
