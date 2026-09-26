using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Penghou.Baize;
using Penghou.Baize.Tools.Extensions;
using Penghou.Nuwa;
using Penghou.Nuwa.Strategies;

namespace Penghou.Baize.Tools.Repair.Tests;

/// <summary>
/// Validation runs after repair in both pipeline points, marks failures
/// InvalidArguments with validator diagnostics, and is replaceable through
/// dependency injection.
/// </summary>
public sealed class ToolArgumentValidationIntegrationTests
{
    private static LlmTool Tool(string schema) => new("lookup", "Lookup", schema);

    [Fact]
    public async Task Normalizer_marks_schema_violations_invalid_with_diagnostics()
    {
        var pipeline = CreatePipeline();
        var normalizer = new LlmResponseNormalizer(
            new ContentToolCallExtractor(pipeline),
            pipeline);
        var response = new LlmResponse(
            Content: string.Empty,
            ToolCalls: [new LlmToolCall("call-1", "lookup", """{"name":"a"}""")]);

        var normalized = await normalizer.NormalizeAsync(
            response,
            [Tool("""{"type":"object","properties":{"name":{"type":"string","minLength":2}}}""")],
            TestContext.Current.CancellationToken);

        var call = normalized.ToolCalls.Should().ContainSingle().Subject;
        call.NormalizationStatus.Should().Be(LlmToolCallNormalizationStatus.InvalidArguments);
        call.ArgumentValidation.Should().NotBeNull();
        call.ArgumentValidation!.IsValid.Should().BeFalse();
        call.ArgumentValidation.ValidatorId.Should().Be(StructuralToolArgumentValidator.ValidatorId);
        call.ArgumentValidation.Failures.Should().ContainSingle()
            .Which.Code.Should().Be("too-short");
        // The repaired JSON is preserved for audit, not dropped.
        call.ArgumentsJson.Should().Be("""{"name":"a"}""");
    }

    [Fact]
    public async Task Normalizer_records_validation_on_valid_calls()
    {
        var pipeline = CreatePipeline();
        var normalizer = new LlmResponseNormalizer(
            new ContentToolCallExtractor(pipeline),
            pipeline);
        var response = new LlmResponse(
            Content: string.Empty,
            ToolCalls: [new LlmToolCall("call-1", "lookup", """{"name":"ada"}""")]);

        var normalized = await normalizer.NormalizeAsync(
            response,
            [Tool("""{"type":"object","properties":{"name":{"type":"string","minLength":2}}}""")],
            TestContext.Current.CancellationToken);

        var call = normalized.ToolCalls.Should().ContainSingle().Subject;
        call.NormalizationStatus.Should().Be(LlmToolCallNormalizationStatus.Normalized);
        call.ArgumentValidation.Should().NotBeNull();
        call.ArgumentValidation!.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validator_is_replaceable_through_injection()
    {
        var pipeline = CreatePipeline();
        var strict = new StrictValidator();
        var normalizer = new LlmResponseNormalizer(
            new ContentToolCallExtractor(pipeline, strict),
            pipeline,
            strict);
        var response = new LlmResponse(
            Content: string.Empty,
            ToolCalls: [new LlmToolCall("call-1", "lookup", """{"name":"ada"}""")]);

        var normalized = await normalizer.NormalizeAsync(
            response,
            [Tool("""{"type":"object"}""")],
            TestContext.Current.CancellationToken);

        var call = normalized.ToolCalls.Should().ContainSingle().Subject;
        call.NormalizationStatus.Should().Be(LlmToolCallNormalizationStatus.InvalidArguments);
        call.ArgumentValidation!.ValidatorId.Should().Be("strict-test/v1");
    }

    [Fact]
    public async Task Extractor_marks_pseudo_call_violations_invalid()
    {
        var pipeline = CreatePipeline();
        var normalizer = new LlmResponseNormalizer(
            new ContentToolCallExtractor(pipeline),
            pipeline);
        var response = new LlmResponse(
            Content: """{"name":"lookup","arguments":{"name":"a"}}""",
            ToolCalls: []);

        var normalized = await normalizer.NormalizeAsync(
            response,
            [Tool("""{"type":"object","properties":{"name":{"type":"string","minLength":2}}}""")],
            TestContext.Current.CancellationToken);

        var call = normalized.ToolCalls.Should().ContainSingle().Subject;
        call.NormalizationStatus.Should().Be(LlmToolCallNormalizationStatus.InvalidArguments);
        call.ArgumentValidation!.ValidatorId.Should().Be(StructuralToolArgumentValidator.ValidatorId);
    }

    [Fact]
    public void Service_collection_registers_structural_default()
    {
        var services = new ServiceCollection();
        services.AddLlmTools();

        var validator = services.BuildServiceProvider()
            .GetRequiredService<ILlmToolArgumentValidator>();
        validator.Should().BeOfType<StructuralToolArgumentValidator>();
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

    private sealed class StrictValidator : ILlmToolArgumentValidator
    {
        public Task<LlmToolArgumentValidationResult> ValidateAsync(
            LlmTool tool,
            string argumentsJson,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new LlmToolArgumentValidationResult(
                false,
                [new LlmToolArgumentValidationFailure("$", "strict-test", "Strict mode rejects everything.")],
                "strict-test/v1"));
    }
}
