using FluentAssertions;
using Penghou.Baize.Tools;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Penghou.Baize.Tools.Repair.Tests;

public sealed class LlmToolFactoryTests
{
    [Fact]
    public void Create_GeneratesInputSchemaFromArgumentType()
    {
        var tool = LlmToolFactory.Create<GetWeatherArguments>(
            "get_weather",
            "Returns the weather for a city");

        tool.Name.Should().Be("get_weather");
        tool.Description.Should().Be("Returns the weather for a city");
        var schema = JsonNode.Parse(tool.InputSchemaJson)!.AsObject();
        schema["properties"]!["city"]!["type"]!.GetValue<string>()
            .Should().Be("string");
        schema["required"]!.AsArray()
            .Select(node => node!.GetValue<string>())
            .Should().Contain("city");
    }

    private sealed class GetWeatherArguments
    {
        [JsonPropertyName("city")]
        public required string City { get; init; }
    }
}
