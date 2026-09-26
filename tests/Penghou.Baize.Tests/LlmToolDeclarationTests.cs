using FluentAssertions;

namespace Penghou.Baize.Tests;

/// <summary>
/// Tool declaration integrity: blank and duplicate names are rejected before
/// provider I/O or repair, case variants stay distinct, and declaration order
/// never selects a winner.
/// </summary>
public sealed class LlmToolDeclarationTests
{
    private static LlmTool Tool(string name, string schema = """{"type":"object"}""") =>
        new(name, "description", schema);

    [Fact]
    public void Validate_accepts_distinct_tools()
    {
        var act = () => LlmToolDeclarations.Validate([Tool("first"), Tool("second")]);
        act.Should().NotThrow();

        var empty = () => LlmToolDeclarations.Validate([]);
        empty.Should().NotThrow();
    }

    [Fact]
    public void Validate_rejects_exact_duplicates_naming_the_tool()
    {
        var act = () => LlmToolDeclarations.Validate(
            [Tool("lookup"), Tool("other", """{"type":"string"}"""), Tool("lookup")]);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*lookup*")
            .Which.ParamName.Should().Be("tools");
    }

    [Fact]
    public void Validate_treats_case_variants_as_distinct_tools()
    {
        var act = () => LlmToolDeclarations.Validate([Tool("Lookup"), Tool("lookup")]);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_rejects_blank_names(string? name)
    {
        var act = () => LlmToolDeclarations.Validate([Tool(name!)]);
        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("tools");
    }

    [Fact]
    public void Validate_rejects_null_collections_and_entries()
    {
        var nullCollection = () => LlmToolDeclarations.Validate(null!);
        nullCollection.Should().Throw<ArgumentNullException>();

        var nullEntry = () => LlmToolDeclarations.Validate([Tool("ok"), null!]);
        nullEntry.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Request_rejects_duplicate_tools_at_construction()
    {
        var act = () => new LlmRequest(
            [new LlmMessage("user", [new LlmTextContent("hi")])],
            tools: [Tool("lookup"), Tool("lookup")]);
        act.Should().Throw<ArgumentException>().WithMessage("*lookup*");
    }
}
