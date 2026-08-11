using FluentAssertions;
using Penghou.Baize.Tools.Schema;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Penghou.Baize.Tools.Repair.Tests;

public sealed class JsonSchemaGeneratorTests
{
    [Fact]
    public void GenerateSchemaNode_DescribesNestedCollectionsAndRequiredMembers()
    {
        var schema = JsonSchemaGenerator.GenerateSchemaNode<Payload>()
            .Should().BeOfType<JsonObject>().Subject;
        var properties = schema["properties"]
            .Should().BeOfType<JsonObject>().Subject;

        properties["name"]!["type"]!.GetValue<string>().Should().Be("string");
        properties["name"]!["description"]!.GetValue<string>()
            .Should().Be("Display name");
        properties["items"]!["type"]!.GetValue<string>().Should().Be("array");
        properties["items"]!["items"]!["properties"]!["count"]!["type"]!
            .GetValue<string>().Should().Be("integer");
        properties["labels"]!["additionalProperties"]!["type"]!
            .GetValue<string>().Should().Be("string");
        schema["required"]!.AsArray()
            .Select(node => node!.GetValue<string>())
            .Should().Contain(["name", "items", "labels"]);
    }

    private sealed class Payload
    {
        [JsonPropertyName("name")]
        [SchemaDescription("Display name")]
        public required string Name { get; init; }

        [JsonPropertyName("items")]
        public required List<Item> Items { get; init; }

        [JsonPropertyName("labels")]
        public required Dictionary<string, string> Labels { get; init; }

        [JsonPropertyName("note")]
        public string? Note { get; init; }
    }

    private sealed class Item
    {
        [JsonPropertyName("count")]
        public required int Count { get; init; }
    }
}
