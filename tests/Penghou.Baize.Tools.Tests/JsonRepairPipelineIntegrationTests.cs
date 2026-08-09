using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Penghou.Nuwa;
using Penghou.Baize.Tools;
using Penghou.Baize.Tools.Extensions;
using System.Text.Json;

namespace Penghou.Baize.Tools.Repair.Tests;

public sealed class JsonRepairPipelineIntegrationTests
{
    [Fact]
    public async Task AddLlmTools_ProvidesConfiguredPipelineToExtractor()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLlmTools();

        using var provider = services.BuildServiceProvider();
        var extractor = provider.GetRequiredService<IContentToolCallExtractor>();

        var calls = await extractor.ExtractAsync(
            """
            ```json
            {
              "name": "emit_files",
              "arguments": {
                "files": []
              }
            }
            ```
            """,
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
                          "items": { "type": "object" }
                        }
                      },
                      "required": ["files"]
                    }
                    """)
            ],
            TestContext.Current.CancellationToken);

        calls.Should().ContainSingle();
        calls[0].Name.Should().Be("emit_files");

        using var arguments = JsonDocument.Parse(calls[0].ArgumentsJson);
        arguments.RootElement.GetProperty("files").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task AddLlmTools_ExtractsQwenBacktickPseudoToolCall()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLlmTools();

        using var provider =
            services.BuildServiceProvider();
        var extractor =
            provider.GetRequiredService<
                IContentToolCallExtractor>();
        var calls = await extractor.ExtractAsync(
            """
            ```json
            {
              "name": "emit_files",
              "arguments": {
                "files": [
                  {
                    "path": "Program.cs",
                    "content": `
            using System;
            var message = "hello";
            `
                  }
                ]
              }
            }
            ```
            """,
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

        calls.Should().ContainSingle();
        calls[0].JsonWasRepaired.Should().BeTrue();
        calls[0].JsonRepairAttempts.Should().Contain(
            attempt =>
                attempt.Name ==
                    "tool-call/pseudo-javascript-template-string" &&
                attempt.Status ==
                    LlmRepairStatus.Succeeded);
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
    public async Task AddLlmTools_ExtractsQwenCallWithInterpolatedString()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLlmTools();

        using var provider =
            services.BuildServiceProvider();
        var extractor =
            provider.GetRequiredService<
                IContentToolCallExtractor>();
        var calls = await extractor.ExtractAsync(
            """
            {
              "name": "emit_files",
              "arguments": {
                "files": [
                  {
                    "path": "GreetingController.cs",
                    "content": "var response = new { message = $"Hello, {name.Trim()}!" };
            return response;
            "
                  }
                ]
              }
            }
            """,
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

        calls.Should().ContainSingle();
        using var arguments =
            JsonDocument.Parse(
                calls[0].ArgumentsJson);
        var source = arguments.RootElement
            .GetProperty("files")[0]
            .GetProperty("content")
            .GetString();
        source.Should().Contain(
            "$\"Hello, {name.Trim()}!\"");
        source.Should().Contain(
            "return response;");
    }

}
