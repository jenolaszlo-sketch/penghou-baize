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

    [Theory]
    [InlineData(LlmSchemaPurpose.ToolInput)]
    [InlineData(LlmSchemaPurpose.StructuredResponse)]
    public void Adapt_InlinesLocalReferencesWithoutChangingCanonicalSchema(
        LlmSchemaPurpose purpose)
    {
        using var source = JsonDocument.Parse(
            """
            {
              "$defs": {
                "artifact": {
                  "type": "object",
                  "properties": {
                    "path": { "type": "string" }
                  },
                  "required": ["path"]
                }
              },
              "type": "object",
              "properties": {
                "artifacts": {
                  "type": "array",
                  "items": { "$ref": "#/$defs/artifact" }
                }
              }
            }
            """);

        var result = GeminiSchemaAdapter.Default.Adapt(
            source.RootElement,
            Context(purpose));

        var raw = result.Schema.GetRawText();
        raw.Should().NotContain("\"$ref\"");
        raw.Should().NotContain("\"$defs\"");
        result.Schema
            .GetProperty("properties")
            .GetProperty("artifacts")
            .GetProperty("items")
            .GetProperty("properties")
            .GetProperty("path")
            .GetProperty("type")
            .GetString()
            .Should().Be("string");
        result.Adaptations.Should().Contain(item =>
            item.Keyword == "$ref" &&
            item.Action == "inlined" &&
            !item.IsLossy);

        source.RootElement.GetRawText().Should().Contain("\"$ref\"");
        source.RootElement.GetRawText().Should().Contain("\"$defs\"");
    }

    [Fact]
    public void Adapt_PreservesReferenceSiblings()
    {
        using var source = JsonDocument.Parse(
            """
            {
              "$defs": {
                "identifier": { "type": "string", "minLength": 1 }
              },
              "type": "object",
              "properties": {
                "id": {
                  "$ref": "#/$defs/identifier",
                  "description": "The public identifier."
                }
              }
            }
            """);

        var result = GeminiSchemaAdapter.Default.Adapt(
            source.RootElement,
            Context(LlmSchemaPurpose.ToolInput));

        var identifier = result.Schema
            .GetProperty("properties")
            .GetProperty("id");
        identifier.GetProperty("type").GetString().Should().Be("string");
        identifier.GetProperty("minLength").GetInt32().Should().Be(1);
        identifier.GetProperty("description").GetString()
            .Should().Be("The public identifier.");
    }

    [Fact]
    public void Adapt_RejectsRecursiveReferencesClearly()
    {
        using var source = JsonDocument.Parse(
            """
            {
              "$defs": {
                "node": {
                  "type": "object",
                  "properties": {
                    "children": {
                      "type": "array",
                      "items": { "$ref": "#/$defs/node" }
                    }
                  }
                }
              },
              "$ref": "#/$defs/node"
            }
            """);

        var action = () => GeminiSchemaAdapter.Default.Adapt(
            source.RootElement,
            Context(LlmSchemaPurpose.ToolInput));

        action.Should().Throw<LlmRequestValidationException>()
            .WithMessage("*recursive JSON Schema reference*#/$defs/node*");
    }

    private static LlmSchemaAdaptationContext Context(
        LlmSchemaPurpose purpose) =>
        new(
            new LlmProviderKey("Gemini"),
            "gemini-3.6-flash",
            "v1beta",
            purpose);
}
