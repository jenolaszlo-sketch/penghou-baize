using FluentAssertions;
using Penghou.Baize.Generation;

namespace Penghou.Baize.Tests;

public sealed class LlmRequestTests
{
    [Fact]
    public void Constructor_NullTools_DefaultsToEmptyList()
    {
        var request = new LlmRequest([new LlmMessage("user", "Hi")]);

        request.Messages.Should().HaveCount(1);
        request.Tools.Should().BeEmpty();
        request.Temperature.Should().BeNull();
        request.MaxTokens.Should().BeNull();
        request.Metadata.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_ProvidesTools_ExposesThem()
    {
        var tool = new LlmTool("get_weather", "Weather lookup", "{}");
        var request = new LlmRequest(
            [new LlmMessage("user", "Weather?"),
             new LlmMessage("assistant", "Let me check.")],
            temperature: 0.7,
            maxTokens: 256,
            tools: [tool]);

        request.Tools.Should().ContainSingle().Which.Should().Be(tool);
        request.Temperature.Should().Be(0.7);
        request.MaxTokens.Should().Be(256);
    }
}

public sealed class LlmResponseTests
{
    [Theory]
    [InlineData("length", LlmFinishReasonKind.LengthLimit)]
    [InlineData("MAX_TOKENS", LlmFinishReasonKind.LengthLimit)]
    [InlineData("MAX_OUTPUT_TOKENS", LlmFinishReasonKind.LengthLimit)]
    [InlineData("stop", LlmFinishReasonKind.Stop)]
    [InlineData("end_turn", LlmFinishReasonKind.Stop)]
    [InlineData("tool_use", LlmFinishReasonKind.ToolCall)]
    [InlineData("content_filter", LlmFinishReasonKind.ContentFilter)]
    [InlineData("provider_specific", LlmFinishReasonKind.Unknown)]
    public void FinishReasonKind_NormalizesProviderReasons(
        string finishReason,
        LlmFinishReasonKind expected)
    {
        new LlmResponse("content", FinishReason: finishReason)
            .FinishReasonKind.Should().Be(expected);
        new LlmStreamEvent(FinishReason: finishReason)
            .FinishReasonKind.Should().Be(expected);
    }

    [Fact]
    public void PositionalConstructor_PreservesOriginalFirstThreeParameters()
    {
        var response = new LlmResponse("content", "reasoning", "stop");

        response.Content.Should().Be("content");
        response.Reasoning.Should().Be("reasoning");
        response.FinishReason.Should().Be("stop");
        response.Parts.Should().BeNull();
    }

    [Fact]
    public void ProviderContinuation_IsForProviderCaseInsensitively()
    {
        var continuation = new LlmProviderContinuation(
            "Claude",
            new Dictionary<string, string>());

        continuation.IsFor("claude").Should().BeTrue();
        continuation.IsFor("Gemini").Should().BeFalse();
    }

    [Fact]
    public void Parts_RemainsAdditiveToOriginalPositionalContract()
    {
        var constructor = typeof(LlmResponse).GetConstructors()
            .Should().ContainSingle().Subject;
        var deconstruct = typeof(LlmResponse).GetMethod("Deconstruct")!;

        constructor.GetParameters().Should().HaveCount(11);
        deconstruct.GetParameters().Should().HaveCount(11);
        new LlmResponse("content").Parts.Should().BeNull();
    }

    [Fact]
    public void StreamPartIndex_RemainsAdditiveToOriginalPositionalContract()
    {
        var constructor = typeof(LlmStreamEvent).GetConstructors()
            .Should().ContainSingle().Subject;
        var deconstruct = typeof(LlmStreamEvent).GetMethod("Deconstruct")!;

        constructor.GetParameters().Should().HaveCount(9);
        deconstruct.GetParameters().Should().HaveCount(9);
        new LlmStreamEvent { PartIndex = 3 }.PartIndex.Should().Be(3);
    }
}

public sealed class LlmMessageTests
{
    [Fact]
    public void TwoArgConstructor_CreatesSingleTextPart()
    {
        var message = new LlmMessage("user", "Hi");

        message.Role.Should().Be("user");
        message.Parts.Should().ContainSingle().Which.Should()
            .BeOfType<LlmTextContent>()
            .Which.Text.Should().Be("Hi");
    }

    [Fact]
    public void TextFactory_CreatesTextMessage()
    {
        var message = LlmMessage.Text("system", "Be concise");

        message.Role.Should().Be("system");
        message.Parts.Should().ContainSingle()
            .Which.Should().Be(new LlmTextContent("Be concise"));
    }

    [Fact]
    public void AssistantFactory_PrependsTextAndAddsToolCalls()
    {
        var toolCall = new LlmToolCall("call_1", "get_weather", """{"city":"Paris"}""");
        var message = LlmMessage.Assistant([toolCall], text: "Let me check.");

        message.Role.Should().Be("assistant");
        message.Parts.Should().HaveCount(2);
        message.Parts[0].Should().Be(new LlmTextContent("Let me check."));
        message.Parts[1].Should().Be(new LlmToolCallContent(toolCall));
    }

    [Fact]
    public void ToolResultsFactory_UsesToolRoleAndResultParts()
    {
        var result = new LlmToolResult("call_1", "get_weather", """{"temp":21}""");
        var message = LlmMessage.ToolResults([result]);

        message.Role.Should().Be("tool");
        message.Parts.Should().ContainSingle()
            .Which.Should().Be(new LlmToolResultContent(result));
    }

    [Fact]
    public void ToolResultFactory_WrapsSingleResult()
    {
        var message = LlmMessage.ToolResult("call_1", "get_weather", "boom", succeeded: false);

        message.Role.Should().Be("tool");
        var part = message.Parts.Should().ContainSingle().Which.Should()
            .BeOfType<LlmToolResultContent>().Which;
        part.Result.ToolCallId.Should().Be("call_1");
        part.Result.ToolName.Should().Be("get_weather");
        part.Result.Content.Should().Be("boom");
        part.Result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public void Message_CanCarryReasoningPart()
    {
        var message = new LlmMessage(
            "assistant",
            [new LlmReasoningContent("thinking"), new LlmTextContent("answer")]);

        message.Parts.Should().HaveCount(2);
        message.Parts[0].Should().Be(new LlmReasoningContent("thinking"));
        message.Parts[1].Should().Be(new LlmTextContent("answer"));
    }
}

public sealed class LlmStreamEventTests
{
    [Fact]
    public void ToolCallDelta_FirstFragment_CarriesIdAndName()
    {
        var delta = new ToolCallDelta(
            Index: 0,
            Id: "call_1",
            Name: "get_weather",
            ArgumentsJsonFragment: "{\"city\"");

        delta.Id.Should().Be("call_1");
        delta.Name.Should().Be("get_weather");
        delta.ArgumentsJsonFragment.Should().Be("{\"city\"");
    }

    [Fact]
    public void StreamEvent_DeltaOnly_HasNullFinishReason()
    {
        var e = new LlmStreamEvent(Delta: "hello");

        e.Delta.Should().Be("hello");
        e.FinishReason.Should().BeNull();
    }
}

public sealed class LlmClientExceptionTests
{
    [Fact]
    public void InnerException_IsPreserved()
    {
        var inner = new InvalidOperationException("boom");
        var ex = new LlmClientException("outer", inner);

        ex.Message.Should().Be("outer");
        ex.InnerException.Should().BeSameAs(inner);
    }

    [Theory]
    [InlineData(400, LlmClientFailureKind.InvalidRequest)]
    [InlineData(401, LlmClientFailureKind.Authentication)]
    [InlineData(403, LlmClientFailureKind.Authorization)]
    [InlineData(404, LlmClientFailureKind.InvalidRequest)]
    [InlineData(429, LlmClientFailureKind.RateLimit)]
    [InlineData(500, LlmClientFailureKind.Availability)]
    [InlineData(503, LlmClientFailureKind.Availability)]
    [InlineData(null, LlmClientFailureKind.Protocol)]
    public void FailureKind_IsDerivedFromStatusCode(
        int? statusCode,
        LlmClientFailureKind expected)
    {
        var ex = new LlmClientException("failure", statusCode);

        ex.FailureKind.Should().Be(expected);
    }

    [Fact]
    public void CanFallback_IsTrueForAvailabilityAndRateLimit()
    {
        new LlmClientException("nope", statusCode: 500).CanFallback.Should().BeTrue();
        new LlmClientException("nope", statusCode: 429).CanFallback.Should().BeTrue();
    }

    [Fact]
    public void CanFallback_IsFalseForDeterministicFailures()
    {
        new LlmClientException("nope", statusCode: 400).CanFallback.Should().BeFalse();
        new LlmClientException("nope", statusCode: 401).CanFallback.Should().BeFalse();
        new LlmClientException("nope", statusCode: 403).CanFallback.Should().BeFalse();
        new LlmClientException("nope", LlmClientFailureKind.InvalidRequest)
            .CanFallback.Should().BeFalse();
        new LlmClientException("nope", LlmClientFailureKind.Content)
            .CanFallback.Should().BeFalse();
    }

    public static TheoryData<int, GenerationErrorKind, LlmClientFailureKind> SharedStatusCodes => new()
    {
        // Both taxonomies must classify shared provider responses identically
        // (401 = Authentication, 403 = Authorization) — this test guards
        // against drift between the two mappers.
        { 401, GenerationErrorKind.Authentication, LlmClientFailureKind.Authentication },
        { 403, GenerationErrorKind.Authorization, LlmClientFailureKind.Authorization },
        { 429, GenerationErrorKind.RateLimited, LlmClientFailureKind.RateLimit },
        { 400, GenerationErrorKind.InvalidRequest, LlmClientFailureKind.InvalidRequest },
        { 404, GenerationErrorKind.InvalidRequest, LlmClientFailureKind.InvalidRequest },
        { 405, GenerationErrorKind.InvalidRequest, LlmClientFailureKind.InvalidRequest },
        { 422, GenerationErrorKind.InvalidRequest, LlmClientFailureKind.InvalidRequest },
        { 408, GenerationErrorKind.ProviderUnavailable, LlmClientFailureKind.Availability },
        { 500, GenerationErrorKind.ProviderUnavailable, LlmClientFailureKind.Availability },
        { 503, GenerationErrorKind.ProviderUnavailable, LlmClientFailureKind.Availability }
    };

    [Theory]
    [MemberData(nameof(SharedStatusCodes))]
    public void SharedStatusCodeClassification_StaysAlignedAcrossTaxonomies(
        int statusCode,
        GenerationErrorKind generationKind,
        LlmClientFailureKind chatKind)
    {
        BaizeException.ClassifyStatusCode(statusCode).Should().Be(generationKind);
        LlmClientException.ClassifyStatusCode(statusCode).Should().Be(chatKind);
    }

    [Fact]
    public void FailureKind_IsSetExplicitlyWithoutStatusCode()
    {
        var ex = new LlmClientException(
            "overloaded",
            LlmClientFailureKind.Availability,
            rateLimit:
                new LlmRateLimitInfo(
                    RetryAfter: TimeSpan.FromSeconds(30)));

        ex.FailureKind.Should().Be(LlmClientFailureKind.Availability);
        ex.StatusCode.Should().BeNull();
        ex.RateLimit.Should().NotBeNull();
        ex.RateLimit!.RetryAfter.Should().Be(TimeSpan.FromSeconds(30));
        ex.CanFallback.Should().BeTrue();
    }

    [Fact]
    public void CanFallback_CanBeOverridden()
    {
        var ex = new LlmClientException(
            "cdn hiccup during parse",
            LlmClientFailureKind.Protocol,
            canFallback: true);

        ex.FailureKind.Should().Be(LlmClientFailureKind.Protocol);
        ex.CanFallback.Should().BeTrue();
    }
}
