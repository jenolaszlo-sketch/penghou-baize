using FluentAssertions;
using Penghou.Nuwa;
using Penghou.Nuwa.Strategies;
using Penghou.Baize.Tools;
using System.Text.Json;

namespace Penghou.Baize.Tools.Repair.Tests;

public sealed class ContentToolCallExtractorRepairTests
{
    [Fact]
    public async Task Extract_UsesToolSchemaForMalformedPseudoToolCall()
    {
        var pipeline = new JsonRepairPipeline(
            [],
            [],
            [
                new SchemaGuidedJsonStringExpansionStrategy()
            ],
            Microsoft.Extensions.Logging.Abstractions
                .NullLogger<JsonRepairPipeline>.Instance);
        var extractor =
            new ContentToolCallExtractor(pipeline);
        var tool = new LlmTool(
            "emit_files",
            "Emits files",
            CreateEmitFilesSchema());
        const string content =
            """
            {
              "name": "emit_files",
              "arguments": {
                "files": [
                  {
                    "path": "Program.cs",
                    "content": using System;
            var message = "hello";
            "
                  }
                ],
                "notes": "done"
              }
            }
            """;

        var calls = await extractor.ExtractAsync(
            content,
            [tool],
            TestContext.Current.CancellationToken);

        calls.Should().ContainSingle();
        calls[0].JsonWasRepaired.Should().BeTrue();
        using var arguments =
            JsonDocument.Parse(
                calls[0].ArgumentsJson);
        arguments.RootElement
            .GetProperty("files")[0]
            .GetProperty("content")
            .GetString()
            .Should()
            .Contain("\"hello\"");
    }

    [Fact]
    public async Task Extract_RecoversFlattenedArgumentsUsingToolSchema()
    {
        var extractor = CreateExtractor();
        const string content =
            """
            ```json
            {
              "name": "emit_files",
              "files": [
                {
                  "path": "Tests.cs",
                  "content": "first"
                },
                {
                  "path": "Tests.cs",
                  "content": "second"
                }
              ]
            }
            ```
            """;

        var calls = await extractor.ExtractAsync(
            content,
            [
                new LlmTool(
                    "emit_files",
                    "Emits files",
                    CreateEmitFilesSchema())
            ],
            TestContext.Current.CancellationToken);

        calls.Should().ContainSingle();
        calls[0].JsonWasRepaired.Should().BeTrue();
        calls[0].JsonRepairAttempts.Should().Contain(
            attempt =>
                attempt.Name ==
                "tool-call/schema-guided-flattened-arguments");
        using var arguments = JsonDocument.Parse(
            calls[0].ArgumentsJson);
        arguments.RootElement
            .GetProperty("files")
            .GetArrayLength()
            .Should()
            .Be(2, "normalization must preserve duplicate paths for downstream validation");
    }

    [Fact]
    public async Task Extract_DoesNotRecoverFlattenedArgumentsWhenShapeIsAmbiguous()
    {
        var extractor = CreateExtractor();
        const string content =
            """
            {
              "name": "emit_files",
              "files": [],
              "unexpected": "value"
            }
            """;

        var calls = await extractor.ExtractAsync(
            content,
            [
                new LlmTool(
                    "emit_files",
                    "Emits files",
                    CreateEmitFilesSchema())
            ],
            TestContext.Current.CancellationToken);

        calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Extract_ExpandsDoubleSerializedArgumentsForNamedTool()
    {
        var extractor = CreateBareExtractor();
        const string content =
            """
            {
              "name": "emit_files",
              "arguments": "{\"files\":[{\"path\":\"Program.cs\",\"content\":\"app.Run();\"}],\"notes\":\"done\"}"
            }
            """;

        var calls = await extractor.ExtractAsync(
            content,
            CreateTwoToolSet(),
            TestContext.Current.CancellationToken);

        calls.Should().ContainSingle();
        calls[0].JsonWasRepaired.Should().BeTrue();
        using var arguments =
            JsonDocument.Parse(calls[0].ArgumentsJson);
        arguments.RootElement.GetProperty("files")[0]
            .GetProperty("content")
            .GetString()
            .Should()
            .Be("app.Run();");
    }

    [Fact]
    public async Task Extract_DoesNotExpandArgumentsBelongingToAnotherTool()
    {
        var extractor = CreateBareExtractor();
        const string content =
            """
            {
              "name": "emit_files",
              "arguments": "{\"repo\":\"acme\",\"count\":3}"
            }
            """;

        var calls = await extractor.ExtractAsync(
            content,
            CreateTwoToolSet(),
            TestContext.Current.CancellationToken);

        calls.Should().ContainSingle();
        calls[0].JsonWasRepaired.Should().BeFalse(
            string.Join(
                "; ",
                calls[0].JsonRepairAttempts?
                    .Select(attempt => $"{attempt.Name}={attempt.Status}") ??
                    Array.Empty<string>()));
        calls[0].ArgumentsJson.Should()
            .Be("{\"repo\":\"acme\",\"count\":3}");
        calls[0].JsonRepairAttempts.Should().NotContain(
            attempt =>
                attempt.Name ==
                    "tool-call/schema-guided-json-string-expansion" &&
                attempt.Status == LlmRepairStatus.Succeeded);
    }

    [Fact]
    public async Task Extract_DoesNotExpandMismatchedArgumentsUnderOtherToolName()
    {
        var extractor = CreateBareExtractor();
        const string content =
            """
            {
              "name": "run_shell",
              "arguments": "{\"files\":[{\"path\":\"A.cs\",\"content\":\"go\"}]}"
            }
            """;

        var calls = await extractor.ExtractAsync(
            content,
            CreateTwoToolSet(),
            TestContext.Current.CancellationToken);

        calls.Should().ContainSingle();
        calls[0].JsonWasRepaired.Should().BeFalse();
        calls[0].ArgumentsJson.Should()
            .Be("{\"files\":[{\"path\":\"A.cs\",\"content\":\"go\"}]}");
    }

    private static ContentToolCallExtractor CreateBareExtractor() =>
        new(
            new JsonRepairPipeline(
                [],
                [],
                [
                    new SchemaGuidedJsonStringExpansionStrategy()
                ],
                Microsoft.Extensions.Logging.Abstractions
                    .NullLogger<JsonRepairPipeline>.Instance));

    private static IReadOnlyCollection<LlmTool> CreateTwoToolSet() =>
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
                    },
                    "notes": { "type": "string" }
                  },
                  "required": ["files"]
                }
                """),
            new LlmTool(
                "run_shell",
                "Runs a shell command",
                """
                {
                  "type": "object",
                  "properties": {
                    "repo": { "type": "string" },
                    "count": { "type": "integer" }
                  },
                  "required": ["repo"]
                }
                """)
        ];

    private static ContentToolCallExtractor CreateExtractor() =>
        new(
            new JsonRepairPipeline(
                [new MarkdownJsonFenceRepairStrategy()],
                [],
                [
                    new SchemaGuidedJsonStringExpansionStrategy()
                ],
                Microsoft.Extensions.Logging.Abstractions
                    .NullLogger<JsonRepairPipeline>.Instance));

    private static string CreateEmitFilesSchema() =>
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
            },
            "notes": { "type": "string" }
          },
          "required": ["files"]
        }
        """;
}
