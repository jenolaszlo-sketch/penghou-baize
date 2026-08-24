using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Penghou.Baize.Tools.Extensions;
using Penghou.Nuwa;
using System.Text.Json;

namespace Penghou.Baize.Tools.Repair.Tests;

public sealed class StructuredOutputRepairTests
{
    private const string NameSchema =
        """
        {
          "type": "object",
          "properties": {
            "name": { "type": "string" }
          },
          "required": ["name"]
        }
        """;

    [Fact]
    public async Task RepairAsync_RepairsMarkdownFencedStructuredOutput()
    {
        var repairer = CreateRepairer();

        var response = await repairer.RepairAsync(
            new LlmResponse("""
                ```json
                {"name": "me"}
                ```
                """),
            LlmResponseFormat.JsonSchema(NameSchema),
            TestContext.Current.CancellationToken);

        response.ContentWasRepaired.Should().BeTrue();
        response.ContentRepairAttempts.Should().Contain(
            attempt =>
                attempt.Name ==
                    "content/markdown-json-fence" &&
                attempt.Status ==
                    LlmRepairStatus.Succeeded);
        response.Content.Should().NotContain("```");
        using var document = JsonDocument.Parse(response.Content);
        document.RootElement.GetProperty("name").GetString()
            .Should().Be("me");
    }

    [Fact]
    public async Task RepairAsync_RepairsMalformedStructuredOutputAgainstSchema()
    {
        var repairer = CreateRepairer();

        var response = await repairer.RepairAsync(
            new LlmResponse(
                """
                {"files":[{"path":"Test.cs","content": using System;
                var message = "hello";
                "}]}
                """),
            LlmResponseFormat.JsonSchema(FilesSchema),
            TestContext.Current.CancellationToken);

        response.ContentWasRepaired.Should().BeTrue();
        response.ContentRepairAttempts.Should().NotBeNull();
        using var document = JsonDocument.Parse(response.Content);
        document.RootElement.GetProperty("files")[0]
            .GetProperty("content").GetString()
            .Should().Contain("using System;");
        JsonSchemaExpectation.FromSchemaJson(FilesSchema)!
            .Validate(System.Text.Json.Nodes.JsonNode.Parse(response.Content)!)
            .Should().BeEmpty();
    }

    [Fact]
    public async Task RepairAsync_RepairsLengthLimitedOutputAndLogsWarning()
    {
        var logger = new RecordingLogger<LlmStructuredOutputRepairer>();
        var repairer = new LlmStructuredOutputRepairer(
            JsonRepairPipeline.Create(),
            logger);

        var response = await repairer.RepairAsync(
            new LlmResponse(
                "{\"name\":\"me",
                FinishReason: "length"),
            LlmResponseFormat.JsonSchema(NameSchema),
            TestContext.Current.CancellationToken);

        response.ContentWasRepaired.Should().BeTrue();
        response.FinishReasonKind.Should().Be(
            LlmFinishReasonKind.LengthLimit);
        response.Content.Should().Be("""{"name":"me"}""");
        logger.Messages.Should().ContainSingle(message =>
            message.Contains("would have failed", StringComparison.Ordinal) &&
            message.Contains("output token limit", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RepairAsync_LeavesSchemaIncompleteTruncationForParserAndLogsReason()
    {
        var logger = new RecordingLogger<LlmStructuredOutputRepairer>();
        var repairer = new LlmStructuredOutputRepairer(
            JsonRepairPipeline.Create(),
            logger);
        const string content = "{\"other\":\"value";

        var response = await repairer.RepairAsync(
            new LlmResponse(content, FinishReason: "max_tokens"),
            LlmResponseFormat.JsonSchema(NameSchema),
            TestContext.Current.CancellationToken);

        response.Content.Should().Be(content);
        response.ContentWasRepaired.Should().BeFalse();
        response.ContentRepairDiagnostics!.ShapeStatus.Should().Be(
            LlmRepairShapeStatus.Mismatched);
        response.ContentRepairDiagnostics.ShapeErrors.Should().Contain(
            "$.name is required.");
        logger.Messages.Should().ContainSingle(message =>
            message.Contains("remained invalid", StringComparison.Ordinal) &&
            message.Contains("output token limit", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RepairAsync_ReturnsUnchangedWhenContentIsValid()
    {
        var repairer = CreateRepairer();
        const string content = """{"name":"me"}""";

        var response = await repairer.RepairAsync(
            new LlmResponse(content),
            LlmResponseFormat.JsonSchema(NameSchema),
            TestContext.Current.CancellationToken);

        response.ContentWasRepaired.Should().BeFalse();
        response.Content.Should().Be(content);
        response.ContentRepairAttempts.Should().NotBeNull();
        response.ContentRepairAttempts!
            .Select(attempt => attempt.Status)
            .Should().NotContain(LlmRepairStatus.Succeeded);
    }

    [Fact]
    public async Task RepairAsync_ReturnsContentUnchangedWhenRepairFails()
    {
        var repairer =
            new LlmStructuredOutputRepairer(
                new FailingPipeline());
        const string content = "not valid json";

        var response = await repairer.RepairAsync(
            new LlmResponse(content),
            LlmResponseFormat.JsonSchema(NameSchema),
            TestContext.Current.CancellationToken);

        response.Content.Should().Be(content);
        response.ContentWasRepaired.Should().BeFalse();
        response.ContentRepairAttempts.Should().Contain(
            attempt =>
                attempt.Name ==
                    "content/example-strategy" &&
                attempt.Status ==
                    LlmRepairStatus.Failed);
    }

    [Fact]
    public async Task RepairAsync_DoesNotApplySalvageThatMismatchesSchema()
    {
        var repairer = CreateRepairer();
        const string content =
            "The requested file could not be produced because the project is too large.";

        var response = await repairer.RepairAsync(
            new LlmResponse(content),
            LlmResponseFormat.JsonSchema(NameSchema),
            TestContext.Current.CancellationToken);

        response.Content.Should().Be(content);
        response.ContentWasRepaired.Should().BeFalse();
        response.ContentRepairDiagnostics!.ShapeStatus
            .Should().Be(LlmRepairShapeStatus.Mismatched);
        response.ContentRepairDiagnostics.ShapeErrors.Should().NotBeEmpty();
        response.ContentRepairAttempts.Should().Contain(
            attempt =>
                attempt.Name ==
                    "content/salvage" &&
                attempt.Status ==
                    LlmRepairStatus.Succeeded);
    }

    [Fact]
    public async Task RepairAsync_ReturnsUnchangedForEmptyContent()
    {
        var repairer = CreateRepairer();

        var response = await repairer.RepairAsync(
            new LlmResponse(string.Empty),
            LlmResponseFormat.JsonSchema(NameSchema),
            TestContext.Current.CancellationToken);

        response.Content.Should().BeEmpty();
        response.ContentWasRepaired.Should().BeFalse();
        response.ContentRepairAttempts.Should().BeNull();
    }

    [Fact]
    public async Task RepairAsync_ReturnsUnchangedWhenNoSchemaIsAvailable()
    {
        var repairer = CreateRepairer();
        const string content = """{"name":"me"}""";

        var response = await repairer.RepairAsync(
            new LlmResponse(content),
            LlmResponseFormat.JsonSchema(string.Empty),
            TestContext.Current.CancellationToken);

        response.Content.Should().Be(content);
        response.ContentWasRepaired.Should().BeFalse();
        response.ContentRepairAttempts.Should().BeNull();
    }

    [Fact]
    public async Task AddLlmTools_ProvidesStructuredOutputRepairer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLlmTools();

        using var provider = services.BuildServiceProvider();
        var repairer = provider.GetRequiredService<ILlmStructuredOutputRepairer>();

        var response = await repairer.RepairAsync(
            new LlmResponse("""
                ```json
                {"name": "me"}
                ```
                """),
            LlmResponseFormat.JsonSchema(NameSchema),
            TestContext.Current.CancellationToken);

        response.ContentWasRepaired.Should().BeTrue();
    }

    [Fact]
    public void AddLlmTools_AppliesCustomNuwaConfiguration()
    {
        var configured = false;
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddLlmTools(_ => configured = true);

        configured.Should().BeTrue();
    }

    private static LlmStructuredOutputRepairer CreateRepairer() =>
        new(JsonRepairPipeline.Create());

    private sealed class FailingPipeline : IJsonRepairPipeline
    {
        public ValueTask<JsonRepairResult> RepairAsync(
            string input,
            JsonSchemaExpectation? expectation = null,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(
                JsonRepairResult.Failure(
                    input,
                    repairedText: null,
                    [
                        new StrategyReport(
                            "example-strategy",
                            StrategyStatus.Failed)
                    ],
                    []));
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning)
                Messages.Add(formatter(state, exception));
        }
    }

    private const string FilesSchema =
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
        """;
}
