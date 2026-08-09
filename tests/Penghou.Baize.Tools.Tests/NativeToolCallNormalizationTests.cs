using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Penghou.Nuwa;
using Penghou.Nuwa.Strategies;
using Penghou.Baize;
using Penghou.Baize.Tools;
using System.Text.Json;

namespace Penghou.Baize.Tools.Repair.Tests;

public sealed class NativeToolCallNormalizationTests
{
    [Fact]
    public async Task Normalize_RemovesNestedOptionalNullBeforeStrictValidation()
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
                    "return_architecture_amendment",
                    """
                    {"taskReplacements":[{"id":"scaffold","moduleId":null}]}
                    """)
            ]);
        var schema =
            """
            {
              "type": "object",
              "properties": {
                "taskReplacements": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": {
                      "id": { "type": "string" },
                      "moduleId": { "type": "string" }
                    },
                    "required": ["id"]
                  }
                }
              },
              "required": ["taskReplacements"]
            }
            """;

        var normalized = await normalizer.NormalizeAsync(
            response,
            [new LlmTool(
                "return_architecture_amendment",
                "Returns an amendment",
                schema)],
            TestContext.Current.CancellationToken);

        normalized.ToolCalls.Should().ContainSingle();
        normalized.ToolCalls![0].JsonWasRepaired.Should().BeTrue();
        normalized.ToolCalls[0].JsonRepairAttempts.Should().Contain(
            attempt =>
                attempt.Name ==
                    "arguments/schema-guided-optional-null-removal" &&
                attempt.Status ==
                    LlmRepairStatus.Succeeded);
        using var arguments = JsonDocument.Parse(
            normalized.ToolCalls[0].ArgumentsJson);
        arguments.RootElement.GetProperty("taskReplacements")[0]
            .TryGetProperty("moduleId", out _).Should().BeFalse();
        JsonSchemaExpectation.FromSchemaJson(schema)!
            .Validate(System.Text.Json.Nodes.JsonNode.Parse(
                arguments.RootElement.GetRawText())!)
            .Should().BeEmpty();
    }

    [Fact]
    public async Task Normalize_RepairsMalformedNativeToolArguments()
    {
        var pipeline = CreatePipeline();
        var extractor =
            new ContentToolCallExtractor(pipeline);
        var normalizer =
            new LlmResponseNormalizer(
                extractor,
                pipeline);
        var response = new LlmResponse(
            Content: string.Empty,
            ToolCalls:
            [
                new LlmToolCall(
                    "call-1",
                    "emit_files",
                    """
                    {"files":[{"path":"Test.cs","content": using System;
                    var message = "hello";
                    "}]}
                    """)
            ]);

        var normalized = await normalizer.NormalizeAsync(
            response,
            [
                new LlmTool(
                    "emit_files",
                    "Emits files",
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
                    """)
            ],
            TestContext.Current.CancellationToken);

        normalized.ToolCalls.Should().ContainSingle();
        normalized.ToolCalls![0].JsonWasRepaired.Should().BeTrue(
            string.Join(
                "; ",
                normalized.ToolCalls[0].JsonRepairAttempts ??
                    Array.Empty<LlmRepairAttempt>()));
        normalized.ToolCalls[0].JsonRepairAttempts.Should()
            .NotBeNull();
        using var arguments = JsonDocument.Parse(
            normalized.ToolCalls[0].ArgumentsJson);
        var content = arguments.RootElement
            .GetProperty("files")[0]
            .GetProperty("content")
            .GetString();
        content.Should().Contain("using System;");
        content.Should().Contain("\"hello\"");
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
