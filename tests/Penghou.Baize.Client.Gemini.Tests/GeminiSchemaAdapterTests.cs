using System.Text.Json;
using FluentAssertions;
using Penghou.Baize;
using Penghou.Baize.Gemini;

namespace Penghou.Baize.Gemini.Tests;

public sealed class GeminiSchemaAdapterTests
{
    [Theory]
    [InlineData(LlmSchemaPurpose.ToolInput)]
    [InlineData(LlmSchemaPurpose.StructuredResponse)]
    public void Adapt_RemovesUnsupportedKeywordRecursively(
        LlmSchemaPurpose purpose)
    {
        using var source = JsonDocument.Parse(
            """
            {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "items": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "additionalProperties": false
                  }
                }
              },
              "anyOf": [
                { "type": "object", "additionalProperties": true }
              ]
            }
            """);

        var result = GeminiSchemaAdapter.Default.Adapt(
            source.RootElement,
            Context(purpose));

        result.WasAdapted.Should().BeTrue();
        result.IsLossy.Should().BeTrue();
        result.Adaptations.Should().HaveCount(3);
        result.Schema.GetRawText().Should().NotContain("\"additionalProperties\":");

        // The caller's canonical schema is authoritative and remains untouched.
        source.RootElement.GetProperty("additionalProperties").GetBoolean()
            .Should().BeFalse();
    }

    [Fact]
    public void Adapt_PreservesPropertyNamedAdditionalProperties()
    {
        using var source = JsonDocument.Parse(
            """
            {
              "type": "object",
              "properties": {
                "additionalProperties": {
                  "type": "object",
                  "additionalProperties": false
                }
              }
            }
            """);

        var result = GeminiSchemaAdapter.Default.Adapt(
            source.RootElement,
            Context(LlmSchemaPurpose.ToolInput));

        var namedProperty = result.Schema
            .GetProperty("properties")
            .GetProperty("additionalProperties");
        namedProperty.GetProperty("type").GetString().Should().Be("object");
        namedProperty.TryGetProperty("additionalProperties", out _)
            .Should().BeFalse();
        result.Adaptations.Should().ContainSingle();
    }

    [Fact]
    public void Adapt_ReportsDeterministicPathAndReason()
    {
        using var source = JsonDocument.Parse(
            """{"type":"object","properties":{"value":{"type":"object","additionalProperties":false}}}""");

        var result = GeminiSchemaAdapter.Default.Adapt(
            source.RootElement,
            Context(LlmSchemaPurpose.ToolInput));

        var adaptation = result.Adaptations.Should().ContainSingle().Subject;
        adaptation.Path.Should().Be("$.properties[\"value\"].additionalProperties");
        adaptation.Keyword.Should().Be("additionalProperties");
        adaptation.Action.Should().Be("removed");
        adaptation.Reason.Should().Contain("Gemini");
        adaptation.IsLossy.Should().BeTrue();
    }

    private static LlmSchemaAdaptationContext Context(
        LlmSchemaPurpose purpose) =>
        new(
            new LlmProviderKey("Gemini"),
            "gemini-3.6-flash",
            "v1beta",
            purpose);
}
