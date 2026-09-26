using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Penghou.Baize;
using Penghou.Nuwa;
using Penghou.Nuwa.Strategies;

namespace Penghou.Baize.Tools.Repair.Tests;

/// <summary>
/// Direct normalizer/extractor callers receive the same deterministic
/// duplicate-declaration failure as ordinary request callers, before any
/// repair or provider I/O.
/// </summary>
public sealed class ToolDeclarationRejectionTests
{
    [Fact]
    public async Task NormalizeAsync_rejects_duplicate_tools()
    {
        var pipeline = CreatePipeline();
        var normalizer = new LlmResponseNormalizer(
            new ContentToolCallExtractor(pipeline),
            pipeline);
        var response = new LlmResponse(Content: "hi", ToolCalls: []);

        var act = () => normalizer.NormalizeAsync(
            response,
            [Tool("lookup"), Tool("lookup")],
            TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<ArgumentException>())
            .WithMessage("*lookup*");
    }

    [Fact]
    public async Task ExtractAsync_rejects_duplicate_tools()
    {
        var pipeline = CreatePipeline();
        var extractor = new ContentToolCallExtractor(pipeline);

        var act = () => extractor.ExtractAsync(
            """{"name":"lookup","arguments":{}}""",
            [Tool("lookup"), Tool("lookup")],
            TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<ArgumentException>())
            .WithMessage("*lookup*");
    }

    [Fact]
    public async Task Entry_points_reject_blank_names()
    {
        var pipeline = CreatePipeline();
        var normalizer = new LlmResponseNormalizer(
            new ContentToolCallExtractor(pipeline),
            pipeline);
        var extractor = new ContentToolCallExtractor(pipeline);
        var response = new LlmResponse(Content: "hi", ToolCalls: []);

        var normalize = () => normalizer.NormalizeAsync(
            response,
            [Tool(" ")],
            TestContext.Current.CancellationToken);
        (await normalize.Should().ThrowAsync<ArgumentException>())
            .Which.ParamName.Should().Be("tools");

        var extract = () => extractor.ExtractAsync(
            "hi",
            [Tool(" ")],
            TestContext.Current.CancellationToken);
        (await extract.Should().ThrowAsync<ArgumentException>())
            .Which.ParamName.Should().Be("tools");
    }

    private static LlmTool Tool(string name) => new(name, "description", """{"type":"object"}""");

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
