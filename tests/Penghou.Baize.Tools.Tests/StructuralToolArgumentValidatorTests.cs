using FluentAssertions;

namespace Penghou.Baize.Tools.Repair.Tests;

/// <summary>
/// Structural validator unit tests: every keyword family, path reporting,
/// invalid inputs, and explicit unsupported-keyword reporting.
/// </summary>
public sealed class StructuralToolArgumentValidatorTests
{
    private static readonly StructuralToolArgumentValidator Validator = new();

    private static LlmTool Tool(string schema) => new("lookup", "description", schema);

    private static async Task<LlmToolArgumentValidationResult> ValidateAsync(string schema, string arguments)
    {
        var result = await Validator.ValidateAsync(Tool(schema), arguments, TestContext.Current.CancellationToken);
        result.ValidatorId.Should().Be(StructuralToolArgumentValidator.ValidatorId);
        return result;
    }

    [Fact]
    public async Task Valid_object_with_required_properties_passes()
    {
        var result = await ValidateAsync(
            """{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}""",
            """{"name":"ada"}""");

        result.IsValid.Should().BeTrue();
        result.Failures.Should().BeEmpty();
        result.UnsupportedKeywords.Should().BeEmpty();
    }

    [Fact]
    public async Task Missing_required_property_reports_path()
    {
        var result = await ValidateAsync(
            """{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}""",
            """{}""");

        result.IsValid.Should().BeFalse();
        result.Failures.Should().ContainSingle(failure => failure.Path == "$.name" && failure.Code == "required-missing");
    }

    [Fact]
    public async Task Wrong_type_reports_mismatch()
    {
        var result = await ValidateAsync(
            """{"type":"object","properties":{"count":{"type":"integer"}}}""",
            """{"count":"three"}""");

        result.IsValid.Should().BeFalse();
        result.Failures.Should().ContainSingle(failure => failure.Path == "$.count" && failure.Code == "type-mismatch");
    }

    [Fact]
    public async Task Union_types_accept_any_member()
    {
        var result = await ValidateAsync(
            """{"type":"object","properties":{"value":{"type":["string","null"]}}}""",
            """{"value":null}""");

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Unknown_schema_type_is_a_schema_error()
    {
        var result = await ValidateAsync(
            """{"type":"object","properties":{"value":{"type":"fanciness"}}}""",
            """{"value":"x"}""");

        result.IsValid.Should().BeFalse();
        result.Failures.Should().Contain(failure => failure.Code == "schema-error");
    }

    [Fact]
    public async Task Closed_object_rejects_unknown_properties()
    {
        var result = await ValidateAsync(
            """{"type":"object","properties":{"a":{"type":"string"}},"additionalProperties":false}""",
            """{"a":"x","b":"y"}""");

        result.IsValid.Should().BeFalse();
        result.Failures.Should().ContainSingle(failure => failure.Path == "$.b" && failure.Code == "unknown-property");
    }

    [Fact]
    public async Task Object_form_additional_properties_is_reported_not_enforced()
    {
        var result = await ValidateAsync(
            """{"type":"object","additionalProperties":{"type":"string"}}""",
            """{"a":"x"}""");

        result.IsValid.Should().BeTrue();
        result.UnsupportedKeywords.Should().Contain("$.additionalProperties");
    }

    [Fact]
    public async Task Enum_and_const_are_enforced()
    {
        var mismatch = await ValidateAsync(
            """{"type":"object","properties":{"mode":{"enum":["a","b"]}}}""",
            """{"mode":"c"}""");
        mismatch.IsValid.Should().BeFalse();
        mismatch.Failures.Should().Contain(failure => failure.Code == "not-in-enum");

        var constant = await ValidateAsync(
            """{"type":"object","properties":{"kind":{"const":"tool"}}}""",
            """{"kind":"other"}""");
        constant.IsValid.Should().BeFalse();
        constant.Failures.Should().Contain(failure => failure.Code == "const-mismatch");
    }

    [Fact]
    public async Task String_length_pattern_and_ranges_are_enforced()
    {
        var schema = """
            {"type":"object","properties":{
              "name":{"type":"string","minLength":2,"maxLength":4,"pattern":"^[a-z]+$"},
              "score":{"type":"number","minimum":0,"exclusiveMaximum":100},
              "tags":{"type":"array","items":{"type":"string"},"minItems":1,"maxItems":2}
            }}
            """;

        var good = await ValidateAsync(schema, """{"name":"ada","score":9.5,"tags":["x"]}""");
        good.IsValid.Should().BeTrue();

        var shortName = await ValidateAsync(schema, """{"name":"a","score":9.5,"tags":["x"]}""");
        shortName.Failures.Should().Contain(failure =>
            failure.Path == "$.name" && failure.Code == "too-short");

        var longName = await ValidateAsync(schema, """{"name":"abcde","score":9.5,"tags":["x"]}""");
        longName.Failures.Should().Contain(failure =>
            failure.Path == "$.name" && failure.Code == "too-long");

        var pattern = await ValidateAsync(schema, """{"name":"A1","score":9.5,"tags":["x"]}""");
        pattern.Failures.Should().Contain(failure =>
            failure.Path == "$.name" && failure.Code == "pattern-mismatch");

        var range = await ValidateAsync(schema, """{"name":"ada","score":100,"tags":["x"]}""");
        range.Failures.Should().Contain(failure =>
            failure.Path == "$.score" && failure.Code == "out-of-range");

        var cardinality = await ValidateAsync(schema, """{"name":"ada","score":9.5,"tags":[]}""");
        cardinality.Failures.Should().Contain(failure =>
            failure.Path == "$.tags" && failure.Code == "too-few-items");

        var element = await ValidateAsync(schema, """{"name":"ada","score":9.5,"tags":[7]}""");
        element.Failures.Should().ContainSingle(failure => failure.Path == "$.tags[0]" && failure.Code == "type-mismatch");
    }

    [Fact]
    public async Task Invalid_pattern_schema_is_a_schema_error()
    {
        var result = await ValidateAsync(
            """{"type":"object","properties":{"name":{"type":"string","pattern":"(["}}}""",
            """{"name":"ada"}""");

        result.IsValid.Should().BeFalse();
        result.Failures.Should().Contain(failure => failure.Code == "schema-error");
    }

    [Fact]
    public async Task Nested_failures_carry_full_paths()
    {
        var result = await ValidateAsync(
            """{"type":"object","properties":{"outer":{"type":"object","properties":{"inner":{"type":"integer"}}}}}""",
            """{"outer":{"inner":"x"}}""");

        result.IsValid.Should().BeFalse();
        result.Failures.Should().ContainSingle()
            .Which.Path.Should().Be("$.outer.inner");
    }

    [Fact]
    public async Task Unsupported_keywords_are_reported_without_failing()
    {
        var result = await ValidateAsync(
            """{"type":"object","properties":{"a":{"type":"string"}},"$ref":"#/defs/x","allOf":[]}""",
            """{"a":"x"}""");

        result.IsValid.Should().BeTrue();
        result.UnsupportedKeywords.Should().BeEquivalentTo("$.$ref", "$.allOf");
    }

    [Fact]
    public async Task Invalid_json_and_broken_schemas_fail()
    {
        var malformed = await ValidateAsync("""{"type":"object"}""", """{"a":}""");
        malformed.IsValid.Should().BeFalse();
        malformed.Failures.Should().ContainSingle()
            .Which.Code.Should().Be("invalid-json");

        var brokenSchema = await ValidateAsync("not json", """{"a":"x"}""");
        brokenSchema.IsValid.Should().BeFalse();
        brokenSchema.Failures.Should().ContainSingle()
            .Which.Code.Should().Be("schema-error");
    }

    [Fact]
    public async Task Integer_rejects_fractions_and_text_accepts_any_string()
    {
        var fraction = await ValidateAsync(
            """{"type":"object","properties":{"n":{"type":"integer"}}}""",
            """{"n":1.5}""");
        fraction.IsValid.Should().BeFalse();

        var whole = await ValidateAsync(
            """{"type":"object","properties":{"n":{"type":"integer"}}}""",
            """{"n":1}""");
        whole.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Object_cardinality_is_enforced()
    {
        var result = await ValidateAsync(
            """{"type":"object","minProperties":1,"maxProperties":1}""",
            """{"a":"x","b":"y"}""");

        result.IsValid.Should().BeFalse();
        result.Failures.Should().ContainSingle()
            .Which.Code.Should().Be("too-many-properties");
    }

    [Fact]
    public void Validate_rejects_null_inputs()
    {
        var nullTool = () => Validator.ValidateAsync(null!, "{}", TestContext.Current.CancellationToken);
        nullTool.Should().ThrowAsync<ArgumentNullException>();

        var nullJson = () => Validator.ValidateAsync(Tool("{}"), "  ", TestContext.Current.CancellationToken);
        nullJson.Should().ThrowAsync<ArgumentException>();
    }
}


