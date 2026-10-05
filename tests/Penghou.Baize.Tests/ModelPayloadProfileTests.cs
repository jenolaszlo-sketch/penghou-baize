using System.Text.Json;
using FluentAssertions;
using Penghou.Model.Abstractions;

namespace Penghou.Baize.Tests;

public sealed class ModelPayloadProfileTests
{
    [Fact]
    public void Snapshot_CopiesSupportedContentToolsDiagnosticsAndPolicyData()
    {
        using var jsonDocument = JsonDocument.Parse("{\"nested\":[1,true]}");
        var continuationValues = new Dictionary<string, string> { ["thought"] = "opaque" };
        var continuation = new LlmProviderContinuation("Gemini", continuationValues);
        var call = new LlmToolCall(
            "call-1", "lookup", "{\"q\":1}", JsonWasRepaired: true,
            JsonRepairAttempts: [new("repair", LlmRepairStatus.Succeeded, "{}", "fixed")],
            Continuation: continuation)
        {
            JsonRepairDiagnostics = new LlmJsonRepairDiagnostics(
                LlmRepairShapeStatus.Matched,
                ["shape ok"],
                "repair",
                new LlmTolerantRecoveryDiagnostics(true, "recovered", 1, 0, ["closed brace"])),
            ArgumentValidation = new LlmToolArgumentValidationResult(
                false,
                [new("$.q", "type", "expected string")],
                "validator",
                ["patternProperties"])
        };
        var bytes = new byte[] { 1, 2, 3 };
        var parts = new LlmContentPart[]
        {
            new LlmTextContent("prompt"),
            new LlmReasoningContent("reasoning"),
            new LlmToolCallContent(call),
            new LlmToolResultContent(new LlmToolResult("call-1", "lookup", "found")),
            new LlmImageContent("image/png", new LlmInlineDataSource(bytes)),
            new LlmAudioContent("audio/wav", new LlmUriSource(new Uri("https://media.example/audio.wav"))),
            new LlmVideoContent("video/mp4", new LlmProviderFileSource("Gemini", "file-1")),
            new LlmFileContent("application/pdf", new LlmInlineDataSource(new byte[] { 4 }), "doc.pdf")
        };
        parts[0] = parts[0] with { Continuation = continuation };
        var context = new ModelExecutionContext("flow", "activity", "agent", "corr");
        var intent = new ModelUsageIntent(estimatedInputTokens: 10, maxOutputTokens: 20);
        var request = new LlmRequest(
            [new LlmMessage("assistant", parts)],
            tools: [new LlmTool("lookup", "Find data", "{\"type\":\"object\"}")],
            responseFormat: LlmResponseFormat.JsonSchema("{\"type\":\"object\"}"),
            thinkingConfig: new LlmThinkingConfig(LlmThinkingMode.Enabled, LlmThinkingEffort.Max),
            metadata: new Dictionary<string, object?> { ["json"] = jsonDocument.RootElement, ["count"] = 2 });
        request = request with { ExecutionContext = context, UsageIntent = intent };

        var snapshot = BaizeModelTransport.Snapshot(request);

        snapshot.Should().NotBeSameAs(request);
        snapshot.Messages.Should().NotBeSameAs(request.Messages);
        snapshot.Messages[0].Parts.Should().NotBeSameAs(request.Messages[0].Parts);
        snapshot.Messages[0].Parts[0].Should().NotBeSameAs(request.Messages[0].Parts[0]);
        snapshot.ExecutionContext.Should().BeSameAs(context);
        snapshot.UsageIntent.Should().BeSameAs(intent);
        snapshot.ResponseFormat!.Schema.Should().Be(request.ResponseFormat!.Schema);
        snapshot.ThinkingConfig.Should().NotBeSameAs(request.ThinkingConfig);
        snapshot.Tools.Should().NotBeSameAs(request.Tools);
        snapshot.Metadata["json"].Should().BeOfType<JsonElement>()
            .Which.GetProperty("nested")[0].GetInt32().Should().Be(1);
        var snapshotCall = ((LlmToolCallContent)snapshot.Messages[0].Parts[2]).ToolCall;
        snapshotCall.Should().NotBeSameAs(call);
        snapshotCall.JsonRepairAttempts.Should().ContainSingle();
        snapshotCall.JsonRepairDiagnostics!.TolerantRecovery!.Corrections.Should().ContainSingle();
        snapshotCall.ArgumentValidation!.Failures.Should().ContainSingle();
        snapshotCall.ArgumentValidation.UnsupportedKeywords.Should().ContainSingle();
        var snapContinuation = snapshot.Messages[0].Parts[0].Continuation!;
        snapContinuation.Values.Should().ContainKey("thought");
        continuationValues["thought"] = "mutated";
        snapContinuation.GetValue("thought").Should().Be("opaque");
        ((LlmInlineDataSource)((LlmImageContent)snapshot.Messages[0].Parts[4]).Source)
            .Data.ToArray().Should().Equal(1, 2, 3);
        ((LlmFileContent)snapshot.Messages[0].Parts[7]).FileName.Should().Be("doc.pdf");
    }

    [Fact]
    public void Snapshot_RejectsExecutableOrUnknownPayloadTypesAndNonFiniteMetadata()
    {
        SnapshotOf(new LlmContentPart[] { new CustomContentPart() })
            .Invoking(BaizeModelTransport.Snapshot)
            .Should().Throw<ArgumentException>().WithMessage("*outside the supported immutable data profile*");
        SnapshotWithMetadata("custom", new object())
            .Invoking(BaizeModelTransport.Snapshot)
            .Should().Throw<ArgumentException>().WithMessage("*outside the supported immutable data profile*");
        SnapshotWithMetadata("number", double.NaN)
            .Invoking(BaizeModelTransport.Snapshot)
            .Should().Throw<ArgumentException>().WithMessage("*outside the supported immutable data profile*");
        SnapshotWithParts(new LlmImageContent("image/png", new CustomMediaSource()))
            .Invoking(BaizeModelTransport.Snapshot)
            .Should().Throw<ArgumentException>().WithMessage("*outside the supported immutable data profile*");
    }

    [Fact]
    public void Snapshot_AcceptsImmutableMetadataScalarsAndClonesJsonAfterSourceDisposal()
    {
        LlmRequest snapshot;
        using (var document = JsonDocument.Parse("{\"ok\":true}"))
        {
            var request = SnapshotWithMetadata(
                "null", null!, "text", "safe", "bool", true,
                "byte", (byte)1, "sbyte", (sbyte)2, "short", (short)3,
                "ushort", (ushort)4, "int", 5, "uint", 6U,
                "long", 7L, "ulong", 8UL, "decimal", 9m,
                "char", 'x', "float", 1.25f, "double", 2.5d,
                "json", document.RootElement);
            snapshot = BaizeModelTransport.Snapshot(request);
        }

        snapshot.Metadata.Should().ContainKey("null").WhoseValue.Should().BeNull();
        snapshot.Metadata["bool"].Should().Be(true);
        snapshot.Metadata["ulong"].Should().Be(8UL);
        snapshot.Metadata["json"].Should().BeOfType<JsonElement>()
            .Which.GetProperty("ok").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void Snapshot_RejectsToolAndDiagnosticCollectionOverflowAndInlineMediaOverLimit()
    {
        var tooManyTools = new LlmRequest(
            [new LlmMessage("user", "x")],
            tools: Enumerable.Range(0, 257)
                .Select(i => new LlmTool($"tool-{i}", "desc", "{}"))
                .ToArray());
        tooManyTools.Invoking(BaizeModelTransport.Snapshot)
            .Should().Throw<ArgumentException>().WithMessage("*at most 256 tool declarations*");

        var tooManyAttempts = new LlmToolCall(
            "call", "tool", "{}",
            JsonRepairAttempts: Enumerable.Range(0, 33)
                .Select(i => new LlmRepairAttempt($"repair-{i}", LlmRepairStatus.Failed))
                .ToArray());
        SnapshotWithParts(new LlmToolCallContent(tooManyAttempts))
            .Invoking(BaizeModelTransport.Snapshot)
            .Should().Throw<ArgumentException>().WithMessage("*Repair attempt lists are limited to 32*");

        var tooManyContinuations = new LlmProviderContinuation(
            "provider", Enumerable.Range(0, 33).ToDictionary(i => $"k{i}", _ => "v"));
        SnapshotWithParts(new LlmTextContent("x") { Continuation = tooManyContinuations })
            .Invoking(BaizeModelTransport.Snapshot)
            .Should().Throw<ArgumentException>().WithMessage("*continuation metadata is limited to 32*");

        var tooManyFailures = new LlmToolCall(
            "call", "tool", "{}")
        {
            ArgumentValidation = new LlmToolArgumentValidationResult(
                false,
                Enumerable.Range(0, 257)
                    .Select(i => new LlmToolArgumentValidationFailure($"$.p{i}", "invalid", "bad"))
                    .ToArray(),
                "validator")
        };
        SnapshotWithParts(new LlmToolCallContent(tooManyFailures))
            .Invoking(BaizeModelTransport.Snapshot)
            .Should().Throw<ArgumentException>().WithMessage("*validation failures are limited to 256*");

        SnapshotWithParts(new LlmImageContent(
                "image/png",
                new LlmInlineDataSource(new byte[64 * 1024 * 1024 + 1])))
            .Invoking(BaizeModelTransport.Snapshot)
            .Should().Throw<ArgumentException>().WithMessage("*Inline media exceeds the supported 64 MiB*");
    }

    [Fact]
    public void Snapshot_EnforcesNestedDiagnosticCollectionLimitsAndJsonDepth()
    {
        var tooManyShapeErrors = new LlmToolCall("call", "tool", "{}")
        {
            JsonRepairDiagnostics = new LlmJsonRepairDiagnostics(
                LlmRepairShapeStatus.Mismatched,
                Enumerable.Range(0, 257).Select(i => $"error-{i}").ToArray())
        };
        SnapshotWithParts(new LlmToolCallContent(tooManyShapeErrors))
            .Invoking(BaizeModelTransport.Snapshot)
            .Should().Throw<ArgumentException>().WithMessage("*Diagnostic string collections are limited to 256*");

        var tooManyCorrections = new LlmToolCall("call", "tool", "{}")
        {
            JsonRepairDiagnostics = new LlmJsonRepairDiagnostics(
                LlmRepairShapeStatus.Matched,
                [],
                TolerantRecovery: new LlmTolerantRecoveryDiagnostics(
                    true, "recovered", 257, 0,
                    Enumerable.Range(0, 257).Select(i => $"correction-{i}").ToArray()))
        };
        SnapshotWithParts(new LlmToolCallContent(tooManyCorrections))
            .Invoking(BaizeModelTransport.Snapshot)
            .Should().Throw<ArgumentException>().WithMessage("*Diagnostic string collections are limited to 256*");

        using var deepDocument = JsonDocument.Parse(
            new string('[', 66) + "0" + new string(']', 66),
            new JsonDocumentOptions { MaxDepth = 70 });
        SnapshotWithMetadata("deep", deepDocument.RootElement)
            .Invoking(BaizeModelTransport.Snapshot)
            .Should().Throw<JsonException>();
    }

    [Fact]
    public void Snapshot_RejectsDisposedJsonAndMalformedUnicode()
    {
        JsonElement disposed;
        using (var document = JsonDocument.Parse("{\"a\":1}"))
            disposed = document.RootElement;

        SnapshotWithMetadata("json", disposed)
            .Invoking(BaizeModelTransport.Snapshot)
            .Should().Throw<ObjectDisposedException>();
        SnapshotOf(new LlmContentPart[] { new LlmTextContent("bad\uD800") })
            .Invoking(BaizeModelTransport.Snapshot)
            .Should().Throw<ArgumentException>().WithMessage("*malformed Unicode*");
    }

    [Fact]
    public void Snapshot_RejectsUnsafeMediaUrisAndPreservesProviderFileAndHttpUriSources()
    {
        SnapshotWithParts(new LlmImageContent("image/png", new LlmUriSource(new Uri("https://user:pass@media.example/x"))))
            .Invoking(BaizeModelTransport.Snapshot)
            .Should().Throw<ArgumentException>().WithMessage("*no user info or fragment*");
        SnapshotWithParts(new LlmImageContent("image/png", new LlmUriSource(new Uri("file:///secret"))))
            .Invoking(BaizeModelTransport.Snapshot)
            .Should().Throw<ArgumentException>().WithMessage("*HTTP(S)*");
        var snapshot = BaizeModelTransport.Snapshot(new LlmRequest(
            [new LlmMessage("user", new LlmContentPart[]
            {
                new LlmImageContent("image/png", new LlmUriSource(new Uri("http://media.example/x"))),
                new LlmFileContent("application/pdf", new LlmProviderFileSource("OpenAi", "file-1"))
            })]));
        ((LlmUriSource)((LlmImageContent)snapshot.Messages[0].Parts[0]).Source).Uri.AbsoluteUri
            .Should().Be("http://media.example/x");
        ((LlmProviderFileSource)((LlmFileContent)snapshot.Messages[0].Parts[1]).Source).FileId
            .Should().Be("file-1");
    }

    [Fact]
    public void Snapshot_EnforcesCollectionAndAggregateLimitsAndTransportAttempt()
    {
        var tooManyMessages = new LlmRequest(Enumerable.Range(0, 1025)
            .Select(_ => new LlmMessage("user", "x")).ToArray());
        tooManyMessages.Invoking(BaizeModelTransport.Snapshot)
            .Should().Throw<ArgumentException>().WithMessage("*at most 1024 messages*");
        var tooManyParts = SnapshotOf(Enumerable.Range(0, 8193)
            .Select(_ => (LlmContentPart)new LlmTextContent(string.Empty)).ToArray());
        tooManyParts.Invoking(BaizeModelTransport.Snapshot)
            .Should().Throw<ArgumentException>().WithMessage("*at most 8192 content parts*");
        SnapshotWithMetadata("a", 1, "b", 2, "c", 3, "d", 4, "e", 5, "f", 6,
            "g", 7, "h", 8, "i", 9, "j", 10, "k", 11, "l", 12,
            "m", 13, "n", 14, "o", 15, "p", 16, "q", 17, "r", 18,
            "s", 19, "t", 20, "u", 21, "v", 22, "w", 23, "x", 24,
            "y", 25, "z", 26, "aa", 27, "ab", 28, "ac", 29,
            "ad", 30, "ae", 31, "af", 32, "ag", 33)
            .Invoking(BaizeModelTransport.Snapshot)
            .Should().Throw<ArgumentException>().WithMessage("*limited to 32 entries*");
        var aggregate = new LlmRequest(Enumerable.Range(0, 17)
            .Select(_ => new LlmMessage("user", new LlmContentPart[]
                { new LlmTextContent(new string('x', 4 * 1024 * 1024)) }))
            .ToArray());
        aggregate.Invoking(BaizeModelTransport.Snapshot)
            .Should().Throw<ArgumentException>().WithMessage("*64 MiB*");
        var invalidAttempt = SnapshotOf([new LlmTextContent("x")]) with { TransportAttempt = 0 };
        invalidAttempt.Invoking(BaizeModelTransport.Snapshot).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void UsageConversions_PreserveUnknownAndCacheCostDetailsAndRejectUnrepresentableCounts()
    {
        BaizeModelTransport.ToModelUsage(null).Should().BeNull();
        BaizeModelTransport.ToLlmUsage(null).Should().BeNull();
        var unknown = BaizeModelTransport.ToModelUsage(new LlmUsage(null, null, null));
        unknown!.InputTokens.Should().BeNull();
        unknown.OutputTokens.Should().BeNull();
        unknown.TotalTokens.Should().BeNull();
        var neutral = new ModelUsage(
            inputTokens: 100,
            cachedInputTokens: 20,
            thinkingTokens: 5,
            cost: 0.25m,
            currency: "USD",
            totalTokens: 100,
            cacheMissInputTokens: 70);
        var llm = BaizeModelTransport.ToLlmUsage(neutral)!;
        llm.PromptTokens.Should().Be(100);
        llm.CompletionTokens.Should().BeNull();
        llm.PromptCacheHitTokens.Should().Be(20);
        llm.PromptCacheMissTokens.Should().Be(70);
        llm.ThinkingTokens.Should().Be(5);
        llm.Cost.Should().Be(0.25m);
        llm.Currency.Should().Be("USD");
        var back = BaizeModelTransport.ToModelUsage(llm)!;
        back.InputTokens.Should().Be(100);
        back.CacheMissInputTokens.Should().Be(70);
        back.Cost.Should().Be(0.25m);
        back.Currency.Should().Be("USD");
        Action outOfRange = () => BaizeModelTransport.ToLlmUsage(
            new ModelUsage(inputTokens: (long)int.MaxValue + 1));
        outOfRange.Should().Throw<LlmClientException>();
        Action negativeUsage = () => _ = new ModelUsage(inputTokens: -1);
        negativeUsage.Should().Throw<ArgumentOutOfRangeException>();
        Action unpairedCost = () => _ = new ModelUsage(cost: 0.1m);
        unpairedCost.Should().Throw<ArgumentException>();
        var invocation = BaizeModelTransport.CreateInvocation(
            new ModelTarget("test", "model", "endpoint"), ModelOperation.Generate,
            new LlmRequest([new LlmMessage("user", "hi")]));
        BaizeModelTransport.VerifyResponseRequestId(invocation, invocation.RequestId);
        Action mismatchedRequest = () =>
            BaizeModelTransport.VerifyResponseRequestId(invocation, "other");
        mismatchedRequest.Should().Throw<LlmClientException>().WithMessage("*different request*");
    }

    private static LlmRequest SnapshotOf(IReadOnlyList<LlmContentPart> parts) =>
        new([new LlmMessage("user", parts)]);

    private static LlmRequest SnapshotWithParts(LlmContentPart part) => SnapshotOf([part]);

    private static LlmRequest SnapshotWithMetadata(params object[] alternatingKeyValues)
    {
        var metadata = new Dictionary<string, object?>();
        for (var index = 0; index < alternatingKeyValues.Length; index += 2)
            metadata.Add((string)alternatingKeyValues[index], alternatingKeyValues[index + 1]);
        return new LlmRequest([new LlmMessage("user", "x")], metadata: metadata);
    }

    private sealed record CustomContentPart : LlmContentPart;

    private sealed record CustomMediaSource : LlmMediaSource
    {
        public override LlmContentTransport Transport => LlmContentTransport.Uri;
    }
}
