using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Penghou.Baize;
using Penghou.Baize.Claude;
using Penghou.Baize.Gemini;
using Penghou.Baize.Ollama;
using Penghou.Baize.OpenAi;
using Penghou.Baize.Router;
using Penghou.Baize.Router.Configuration;
using Penghou.Baize.Router.Extensions;
using ServiceCollectionExtensions = Penghou.Baize.Router.Extensions.ServiceCollectionExtensions;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Penghou.Baize.Router.Tests;

public sealed class LlmRouterTests
{
    [Fact]
    public async Task CompleteStreamingAsync_PreservesOrderedPartsAndLateContinuations()
    {
        var geminiSignature = new LlmProviderContinuation(
            "Gemini",
            new Dictionary<string, string> { ["thoughtSignature"] = "sig-text" });
        var claudeSignature = new LlmProviderContinuation(
            "Claude",
            new Dictionary<string, string> { ["signature"] = "sig-thinking" });
        var client = new EventClient(
        [
            new LlmStreamEvent(Delta: "before") { PartIndex = 0 },
            // Gemini can deliver a signature in a final empty-text chunk.
            new LlmStreamEvent(
                Delta: string.Empty,
                Continuation: geminiSignature) { PartIndex = 0 },
            new LlmStreamEvent(
                ToolCallDelta: new ToolCallDelta(
                    Index: 0,
                    Id: "call-1",
                    Name: "lookup",
                    ArgumentsJsonFragment: "{}")) { PartIndex = 1 },
            // Claude omitted thinking has no visible text, only a signature.
            new LlmStreamEvent(
                ReasoningContent: string.Empty,
                Continuation: claudeSignature) { PartIndex = 2 },
            new LlmStreamEvent(Delta: "after") { PartIndex = 3 },
            new LlmStreamEvent(FinishReason: "stop")
        ]);
        var router = new LlmRouter(
            new LlmModelLookup(
                new Dictionary<string, Func<ILlmClient>> { ["m"] = () => client },
                new Dictionary<(string, ApiStyle), Func<ILlmClient>>
                {
                    [("m", ApiStyle.OpenAi)] = () => client
                }),
            new Dictionary<ModelStrategy, IReadOnlyList<string>>());

        var response = await router.CompleteStreamingAsync(
            "m",
            new LlmPromptBuilder
            {
                Messages = [new LlmMessage("user", "test")]
            },
            cancellationToken: TestContext.Current.CancellationToken);

        response.Content.Should().Be("beforeafter");
        response.Parts.Should().HaveCount(4);
        response.Parts![0].Should().BeOfType<LlmTextContent>()
            .Which.Continuation.Should().BeSameAs(geminiSignature);
        response.Parts[1].Should().BeOfType<LlmToolCallContent>()
            .Which.ToolCall.Name.Should().Be("lookup");
        response.Parts[2].Should().BeOfType<LlmReasoningContent>()
            .Which.Should().BeEquivalentTo(
                new LlmReasoningContent(string.Empty)
                {
                    Continuation = claudeSignature
                });
        response.Parts[3].Should().BeOfType<LlmTextContent>()
            .Which.Text.Should().Be("after");
    }

    [Fact]
    public async Task CompleteStreamingAsync_UsesSameGeneratedToolCallInBothProjections()
    {
        var client = new EventClient(
        [
            new LlmStreamEvent(
                ToolCallDelta: new ToolCallDelta(
                    Index: 0,
                    Name: "lookup",
                    ArgumentsJsonFragment: "{}")) { PartIndex = 0 },
            new LlmStreamEvent(FinishReason: "tool_calls")
        ]);
        var router = new LlmRouter(
            new LlmModelLookup(
                new Dictionary<string, Func<ILlmClient>> { ["m"] = () => client },
                new Dictionary<(string, ApiStyle), Func<ILlmClient>>
                {
                    [("m", ApiStyle.Ollama)] = () => client
                }),
            new Dictionary<ModelStrategy, IReadOnlyList<string>>());

        var response = await router.CompleteStreamingAsync(
            "m",
            new LlmPromptBuilder
            {
                Messages = [new LlmMessage("user", "test")]
            },
            cancellationToken: TestContext.Current.CancellationToken);

        var projected = response.ToolCalls.Should().ContainSingle().Subject;
        var part = response.Parts.Should().ContainSingle().Subject
            .Should().BeOfType<LlmToolCallContent>().Subject.ToolCall;

        part.Should().BeSameAs(projected);
        part.Id.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Router_StrategySkipsUnregisteredModelAndUsesNextCandidate()
    {
        var client = new StubClient("fallback result");
        var router = new LlmRouter(
            new LlmModelLookup(
                new Dictionary<string, Func<ILlmClient>>
                {
                    ["granite-native"] = () => client
                },
                new Dictionary<(string Model, ApiStyle ApiStyle), Func<ILlmClient>>
                {
                    [("granite-native", ApiStyle.Ollama)] = () => client
                }),
            new Dictionary<ModelStrategy, IReadOnlyList<string>>
            {
                [ModelStrategy.Auto] = ["unregistered-model", "granite-native"]
            });

        var response = await router.CompleteStreamingAsync(
            ModelStrategy.Auto,
            new LlmPromptBuilder
            {
                Messages = [new LlmMessage("user", "Say hi")]
            },
            cancellationToken: TestContext.Current.CancellationToken);

        response.Content.Should().Be("fallback result");
    }

    [Fact]
    public async Task Router_StrategyPrefersLeastFailingEndpoint()
    {
        var failingClient = new StubClient("from model a");
        var healthyClient = new StubClient("from model b");

        var lookup = new LlmModelLookup(
            new Dictionary<string, Func<ILlmClient>>
            {
                ["model-a"] = () => failingClient,
                ["model-b"] = () => healthyClient
            },
            new Dictionary<(string Model, ApiStyle ApiStyle), Func<ILlmClient>>
            {
                [("model-a", ApiStyle.Ollama)] = () => failingClient,
                [("model-b", ApiStyle.Ollama)] = () => healthyClient
            });
        var memory = new InMemoryLlmRouterMemory();
        await memory.RecordFailureAsync(
            "model-a:Ollama",
            LlmFailureCategory.Availability,
            cancellationToken: TestContext.Current.CancellationToken);
        var router = new LlmRouter(
            lookup,
            new Dictionary<ModelStrategy, IReadOnlyList<string>>
            {
                [ModelStrategy.Auto] = ["model-a", "model-b"]
            },
            memory);

        var response = await router.CompleteStreamingAsync(
            ModelStrategy.Auto,
            new LlmPromptBuilder
            {
                Messages = [new LlmMessage("user", "Say hi")]
            },
            cancellationToken: TestContext.Current.CancellationToken);

        response.Content.Should().Be("from model b");
    }

    [Fact]
    public async Task Router_ModelPrefersLeastFailingEndpointAcrossStyles()
    {
        var ollamaEndpointClient = new StubClient("from ollama endpoint");
        var claudeEndpointClient = new StubClient("from claude endpoint");

        var lookup = new LlmModelLookup(
            new Dictionary<string, Func<ILlmClient>>
            {
                ["multi"] = () => ollamaEndpointClient
            },
            new Dictionary<(string Model, ApiStyle ApiStyle), Func<ILlmClient>>
            {
                [("multi", ApiStyle.Ollama)] = () => ollamaEndpointClient,
                [("multi", ApiStyle.Claude)] = () => claudeEndpointClient
            });
        var memory = new InMemoryLlmRouterMemory();
        await memory.RecordCallAsync("multi:Ollama", TestContext.Current.CancellationToken);
        await memory.RecordFailureAsync(
            "multi:Ollama",
            LlmFailureCategory.ToolRepairNeeded,
            cancellationToken: TestContext.Current.CancellationToken);
        var router = new LlmRouter(
            lookup,
            new Dictionary<ModelStrategy, IReadOnlyList<string>>(),
            memory);

        router.Resolve("multi")
            .Should().Be(new ResolvedEndpoint("multi:Claude", "multi", ApiStyle.Claude));

        var response = await router.CompleteStreamingAsync(
            "multi",
            new LlmPromptBuilder
            {
                Messages = [new LlmMessage("user", "Say hi")]
            },
            cancellationToken: TestContext.Current.CancellationToken);

        response.Content.Should().Be("from claude endpoint");
    }

    [Fact]
    public async Task Router_SameModelTwoEndpoints_SameStyleKeepSeparateStats()
    {
        var primary = new StubClient("from primary");
        var backup = new StubClient("from backup");
        var lookup = new LlmModelLookup(
            new Dictionary<string, Func<ILlmClient>>
            {
                ["model-a"] = () => primary
            },
            new Dictionary<(string Model, ApiStyle ApiStyle), Func<ILlmClient>>
            {
                [("model-a", ApiStyle.OpenAi)] = () => primary
            },
            stylesByModel: new Dictionary<string, IReadOnlyList<ApiStyle>>
            {
                ["model-a"] = [ApiStyle.OpenAi]
            },
            byEndpointId: new Dictionary<string, Func<ILlmClient>>
            {
                ["primary-gateway"] = () => primary,
                ["backup-gateway"] = () => backup
            },
            endpointsByModel: new Dictionary<string, IReadOnlyList<ResolvedEndpoint>>
            {
                ["model-a"] =
                [
                    new ResolvedEndpoint("primary-gateway", "model-a", ApiStyle.OpenAi),
                    new ResolvedEndpoint("backup-gateway", "model-a", ApiStyle.OpenAi)
                ]
            });
        var memory = new InMemoryLlmRouterMemory();
        var router = new LlmRouter(
            lookup,
            new Dictionary<ModelStrategy, IReadOnlyList<string>>(),
            memory);

        // Only the primary endpoint has failed, so the router must pick the
        // backup even though both endpoints share the same (model, style).
        await memory.RecordFailureAsync(
            "primary-gateway",
            LlmFailureCategory.Availability,
            cancellationToken: TestContext.Current.CancellationToken);

        router.Resolve("model-a")
            .Should().Be(new ResolvedEndpoint("backup-gateway", "model-a", ApiStyle.OpenAi));

        var response = await router.CompleteStreamingAsync(
            "model-a",
            new LlmPromptBuilder
            {
                Messages = [new LlmMessage("user", "Say hi")]
            },
            cancellationToken: TestContext.Current.CancellationToken);

        response.Content.Should().Be("from backup");

        var primaryStats = await memory.GetStatsAsync(
            "primary-gateway",
            TestContext.Current.CancellationToken);
        primaryStats.AvailabilityFailures.Should().Be(1);
        primaryStats.TotalCalls.Should().Be(0);

        var backupStats = await memory.GetStatsAsync(
            "backup-gateway",
            TestContext.Current.CancellationToken);
        backupStats.AvailabilityFailures.Should().Be(0);
        backupStats.TotalCalls.Should().Be(1);
    }

    [Fact]
    public void LlmModelLookup_GetClientByEndpointIdReturnsDistinctClients()
    {
        var primary = new StubClient("from primary");
        var backup = new StubClient("from backup");
        var lookup = new LlmModelLookup(
            new Dictionary<string, Func<ILlmClient>>
            {
                ["model-a"] = () => primary
            },
            new Dictionary<(string Model, ApiStyle ApiStyle), Func<ILlmClient>>(),
            byEndpointId: new Dictionary<string, Func<ILlmClient>>
            {
                ["primary-gateway"] = () => primary,
                ["backup-gateway"] = () => backup
            });

        lookup.GetClientByEndpointId("primary-gateway").Should().BeSameAs(primary);
        lookup.GetClientByEndpointId("backup-gateway").Should().BeSameAs(backup);
    }

    [Fact]
    public void TryValidate_RejectsDuplicateEndpointIds()
    {
        var options = new LlmRoutingOptions
        {
            Models =
            [
                new LlmModelOptions
                {
                    Name = "m",
                    Endpoints =
                    [
                        new LlmEndpointOptions
                        {
                            Id = "gateway",
                            ApiStyle = ApiStyle.OpenAi
                        },
                        new LlmEndpointOptions
                        {
                            Id = "gateway",
                            ApiStyle = ApiStyle.Claude
                        }
                    ]
                }
            ]
        };

        ServiceCollectionExtensions.TryValidate(
            options,
            out var error).Should().BeFalse();
        error.Should().Contain("Duplicate endpoint id: 'gateway'");
    }

    [Fact]
    public async Task Router_RecordsCallForStreamedEndpoint()
    {
        var client = new StubClient("ok");
        var lookup = new LlmModelLookup(
            new Dictionary<string, Func<ILlmClient>>
            {
                ["model-a"] = () => client
            },
            new Dictionary<(string Model, ApiStyle ApiStyle), Func<ILlmClient>>
            {
                [("model-a", ApiStyle.Ollama)] = () => client
            });
        var memory = new InMemoryLlmRouterMemory();
        var router = new LlmRouter(
            lookup,
            new Dictionary<ModelStrategy, IReadOnlyList<string>>(),
            memory);

        await router.CompleteStreamingAsync(
            "model-a",
            new LlmPromptBuilder
            {
                Messages = [new LlmMessage("user", "Say hi")]
            },
            cancellationToken: TestContext.Current.CancellationToken);

        var stats = await memory.GetStatsAsync(
            "model-a:Ollama",
            TestContext.Current.CancellationToken);
        stats.TotalCalls.Should().Be(1);
    }

    [Fact]
    public async Task InMemoryRouterMemory_AvailabilityIsWindowedButQualityCountersAreCumulative()
    {
        var memory = new InMemoryLlmRouterMemory(
            availabilityWindow: TimeSpan.FromMilliseconds(20));

        await memory.RecordCallAsync("m:Ollama", TestContext.Current.CancellationToken);
        await memory.RecordFailureAsync(
            "m:Ollama",
            LlmFailureCategory.ToolRepairNeeded,
            cancellationToken: TestContext.Current.CancellationToken);
        await memory.RecordFailureAsync(
            "m:Ollama",
            LlmFailureCategory.StructuredOutputMismatch,
            cancellationToken: TestContext.Current.CancellationToken);
        await memory.RecordFailureAsync(
            "m:Ollama",
            LlmFailureCategory.Availability,
            cancellationToken: TestContext.Current.CancellationToken);

        var stats = await memory.GetStatsAsync("m:Ollama", TestContext.Current.CancellationToken);
        stats.TotalCalls.Should().Be(1);
        stats.ToolRepairFailures.Should().Be(1);
        stats.StructuredOutputFailures.Should().Be(1);
        stats.AvailabilityFailures.Should().Be(1);

        await Task.Delay(80, TestContext.Current.CancellationToken);

        stats = await memory.GetStatsAsync("m:Ollama", TestContext.Current.CancellationToken);
        stats.AvailabilityFailures.Should().Be(0);
        stats.ToolRepairFailures.Should().Be(1);
        stats.StructuredOutputFailures.Should().Be(1);
    }

    [Fact]
    public async Task Router_SkipsEndpointUnderRateLimitCooldown()
    {
        var blockedClient = new StubClient("from blocked");
        var healthyClient = new StubClient("from healthy");
        var lookup = new LlmModelLookup(
            new Dictionary<string, Func<ILlmClient>>
            {
                ["model-a"] = () => blockedClient,
                ["model-b"] = () => healthyClient
            },
            new Dictionary<(string Model, ApiStyle ApiStyle), Func<ILlmClient>>
            {
                [("model-a", ApiStyle.Ollama)] = () => blockedClient,
                [("model-b", ApiStyle.Ollama)] = () => healthyClient
            });
        var memory = new InMemoryLlmRouterMemory();
        await memory.RecordFailureAsync(
            "model-a:Ollama",
            LlmFailureCategory.Availability,
            unavailableUntil: DateTimeOffset.UtcNow.AddMinutes(5),
            cancellationToken: TestContext.Current.CancellationToken);
        var router = new LlmRouter(
            lookup,
            new Dictionary<ModelStrategy, IReadOnlyList<string>>
            {
                [ModelStrategy.Auto] = ["model-a", "model-b"]
            },
            memory);

        var response = await router.CompleteStreamingAsync(
            ModelStrategy.Auto,
            new LlmPromptBuilder
            {
                Messages = [new LlmMessage("user", "Say hi")]
            },
            cancellationToken: TestContext.Current.CancellationToken);

        response.Content.Should().Be("from healthy");
    }

    [Fact]
    public async Task Router_ExpiredCooldownAllowsSelection()
    {
        var client = new StubClient("from a");
        var lookup = new LlmModelLookup(
            new Dictionary<string, Func<ILlmClient>>
            {
                ["model-a"] = () => client
            },
            new Dictionary<(string Model, ApiStyle ApiStyle), Func<ILlmClient>>
            {
                [("model-a", ApiStyle.Ollama)] = () => client
            });
        var memory = new InMemoryLlmRouterMemory();
        await memory.RecordFailureAsync(
            "model-a:Ollama",
            LlmFailureCategory.Availability,
            unavailableUntil: DateTimeOffset.UtcNow.AddSeconds(-10),
            cancellationToken: TestContext.Current.CancellationToken);
        var router = new LlmRouter(
            lookup,
            new Dictionary<ModelStrategy, IReadOnlyList<string>>
            {
                [ModelStrategy.Auto] = ["model-a"]
            },
            memory);

        router.Resolve(ModelStrategy.Auto)
            .Should().Be(new ResolvedEndpoint("model-a:Ollama", "model-a", ApiStyle.Ollama));
    }

    [Fact]
    public async Task Router_FallsBackToNextEndpointWithinCall()
    {
        var rateLimitedClient = new ThrowingClient(
            new LlmClientException(
                "rate limited",
                statusCode: 429,
                new LlmRateLimitInfo(
                    RetryAfter: TimeSpan.FromSeconds(30))));
        var healthyClient = new StubClient("from healthy");
        var lookup = new LlmModelLookup(
            new Dictionary<string, Func<ILlmClient>>
            {
                ["model-a"] = () => rateLimitedClient,
                ["model-b"] = () => healthyClient
            },
            new Dictionary<(string Model, ApiStyle ApiStyle), Func<ILlmClient>>
            {
                [("model-a", ApiStyle.Ollama)] = () => rateLimitedClient,
                [("model-b", ApiStyle.Ollama)] = () => healthyClient
            });
        var memory = new InMemoryLlmRouterMemory();
        var router = new LlmRouter(
            lookup,
            new Dictionary<ModelStrategy, IReadOnlyList<string>>
            {
                [ModelStrategy.Auto] = ["model-a", "model-b"]
            },
            memory);

        router.Resolve(ModelStrategy.Auto)
            .Should().Be(new ResolvedEndpoint("model-a:Ollama", "model-a", ApiStyle.Ollama));

        var response = await router.CompleteStreamingAsync(
            ModelStrategy.Auto,
            new LlmPromptBuilder
            {
                Messages = [new LlmMessage("user", "Say hi")]
            },
            cancellationToken: TestContext.Current.CancellationToken);

        response.Content.Should().Be("from healthy");

        var stats = await memory.GetStatsAsync(
            "model-a:Ollama",
            TestContext.Current.CancellationToken);
        stats.AvailabilityFailures.Should().Be(1);
        stats.UnavailableUntil.Should().NotBeNull();
        stats.UnavailableUntil!.Value.Should().BeAfter(DateTimeOffset.UtcNow);

        router.Resolve(ModelStrategy.Auto)
            .Should().Be(new ResolvedEndpoint("model-b:Ollama", "model-b", ApiStyle.Ollama));

        response.RouterDiagnostics.Should().NotBeNull();
        response.RouterDiagnostics!.Attempts.Should().HaveCount(2);
        response.RouterDiagnostics.Attempts[0].Outcome
            .Should().Be(LlmRouterAttemptOutcome.Failed);
        response.RouterDiagnostics.Attempts[0].EndpointModel.Should().Be("model-a");
        response.RouterDiagnostics.Attempts[0].UnavailableUntil.Should().NotBeNull();
        response.RouterDiagnostics.Attempts[1].Outcome
            .Should().Be(LlmRouterAttemptOutcome.Succeeded);
        response.RouterDiagnostics.Attempts[1].EndpointModel.Should().Be("model-b");
    }

    [Fact]
    public async Task Router_DoesNotFallBackAfterContentEmitted()
    {
        var partialClient = new EmitThenFailClient(
            "partial ",
            new LlmClientException("server error", statusCode: 500));
        var healthyClient = new StubClient("from healthy");
        var lookup = new LlmModelLookup(
            new Dictionary<string, Func<ILlmClient>>
            {
                ["model-a"] = () => partialClient,
                ["model-b"] = () => healthyClient
            },
            new Dictionary<(string Model, ApiStyle ApiStyle), Func<ILlmClient>>
            {
                [("model-a", ApiStyle.Ollama)] = () => partialClient,
                [("model-b", ApiStyle.Ollama)] = () => healthyClient
            });
        var memory = new InMemoryLlmRouterMemory();
        var router = new LlmRouter(
            lookup,
            new Dictionary<ModelStrategy, IReadOnlyList<string>>
            {
                [ModelStrategy.Auto] = ["model-a", "model-b"]
            },
            memory);

        var action = async () =>
            await router.CompleteStreamingAsync(
                ModelStrategy.Auto,
                new LlmPromptBuilder
                {
                    Messages = [new LlmMessage("user", "Say hi")]
                },
                cancellationToken: TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<LlmClientException>()
            .Where(exception => exception.StatusCode == 500);

        var healthyStats = await memory.GetStatsAsync(
            "model-b:Ollama",
            TestContext.Current.CancellationToken);
        healthyStats.TotalCalls.Should().Be(0);

        var partialStats = await memory.GetStatsAsync(
            "model-a:Ollama",
            TestContext.Current.CancellationToken);
        partialStats.AvailabilityFailures.Should().Be(1);
    }

    [Fact]
    public async Task Router_SkipsIncompatibleCandidateAndFallsBackToNext()
    {
        var incompatibleClient = new ValidationThrowingClient(
            new LlmRequestValidationException(
                "The endpoint 'model-a' does not support native tool calling."));
        var healthyClient = new StubClient("from healthy");
        var lookup = new LlmModelLookup(
            new Dictionary<string, Func<ILlmClient>>
            {
                ["model-a"] = () => incompatibleClient,
                ["model-b"] = () => healthyClient
            },
            new Dictionary<(string Model, ApiStyle ApiStyle), Func<ILlmClient>>
            {
                [("model-a", ApiStyle.Ollama)] = () => incompatibleClient,
                [("model-b", ApiStyle.Ollama)] = () => healthyClient
            });
        var memory = new InMemoryLlmRouterMemory();
        var router = new LlmRouter(
            lookup,
            new Dictionary<ModelStrategy, IReadOnlyList<string>>
            {
                [ModelStrategy.ToolCall] = ["model-a", "model-b"]
            },
            memory);

        var response = await router.CompleteStreamingAsync(
            ModelStrategy.ToolCall,
            new LlmPromptBuilder
            {
                Messages = [new LlmMessage("user", "Say hi")]
            },
            cancellationToken: TestContext.Current.CancellationToken);

        response.Content.Should().Be("from healthy");

        response.RouterDiagnostics.Should().NotBeNull();
        response.RouterDiagnostics!.Attempts.Should().HaveCount(2);
        response.RouterDiagnostics.Attempts[0].Outcome
            .Should().Be(LlmRouterAttemptOutcome.Failed);
        response.RouterDiagnostics.Attempts[0].EndpointModel.Should().Be("model-a");

        // A capability mismatch is not an availability failure: it records no
        // cooldown, so the incompatible candidate remains eligible.
        var incompatibleStats = await memory.GetStatsAsync(
            "model-a:Ollama",
            TestContext.Current.CancellationToken);
        incompatibleStats.AvailabilityFailures.Should().Be(0);

        response.RouterDiagnostics.Attempts[1].Outcome
            .Should().Be(LlmRouterAttemptOutcome.Succeeded);
        response.RouterDiagnostics.Attempts[1].EndpointModel.Should().Be("model-b");
    }

    [Fact]
    public async Task Router_FallsBackOnlyOnAvailabilityFailure()
    {
        var badRequestClient = new ThrowingClient(
            new LlmClientException("bad request", statusCode: 400));
        var healthyClient = new StubClient("from healthy");
        var lookup = new LlmModelLookup(
            new Dictionary<string, Func<ILlmClient>>
            {
                ["model-a"] = () => badRequestClient,
                ["model-b"] = () => healthyClient
            },
            new Dictionary<(string Model, ApiStyle ApiStyle), Func<ILlmClient>>
            {
                [("model-a", ApiStyle.Ollama)] = () => badRequestClient,
                [("model-b", ApiStyle.Ollama)] = () => healthyClient
            });
        var memory = new InMemoryLlmRouterMemory();
        var router = new LlmRouter(
            lookup,
            new Dictionary<ModelStrategy, IReadOnlyList<string>>
            {
                [ModelStrategy.Auto] = ["model-a", "model-b"]
            },
            memory);

        var action = async () =>
            await router.CompleteStreamingAsync(
                ModelStrategy.Auto,
                new LlmPromptBuilder
                {
                    Messages = [new LlmMessage("user", "Say hi")]
                },
                cancellationToken: TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<LlmClientException>()
            .Where(exception => exception.StatusCode == 400);

        var healthyStats = await memory.GetStatsAsync(
            "model-b:Ollama",
            TestContext.Current.CancellationToken);
        healthyStats.TotalCalls.Should().Be(0);

        var badRequestStats = await memory.GetStatsAsync(
            "model-a:Ollama",
            TestContext.Current.CancellationToken);
        badRequestStats.AvailabilityFailures.Should().Be(0);
    }

    [Fact]
    public async Task Router_FallsBackOnStatuslessAvailabilityFailure()
    {
        // Claude's in-stream overloaded_error, Ollama's premature stream end
        // and Gemini's empty stream all surface without an HTTP status code;
        // the explicit failure kind must still trigger fallback.
        var overloadedClient = new ThrowingClient(
            new LlmClientException(
                "Claude streaming error (overloaded_error): Overloaded",
                LlmClientFailureKind.Availability,
                rateLimit:
                    new LlmRateLimitInfo(
                        RetryAfter: TimeSpan.FromSeconds(30))));
        var healthyClient = new StubClient("from healthy");
        var lookup = new LlmModelLookup(
            new Dictionary<string, Func<ILlmClient>>
            {
                ["model-a"] = () => overloadedClient,
                ["model-b"] = () => healthyClient
            },
            new Dictionary<(string Model, ApiStyle ApiStyle), Func<ILlmClient>>
            {
                [("model-a", ApiStyle.Ollama)] = () => overloadedClient,
                [("model-b", ApiStyle.Ollama)] = () => healthyClient
            });
        var memory = new InMemoryLlmRouterMemory();
        var router = new LlmRouter(
            lookup,
            new Dictionary<ModelStrategy, IReadOnlyList<string>>
            {
                [ModelStrategy.Auto] = ["model-a", "model-b"]
            },
            memory);

        var response = await router.CompleteStreamingAsync(
            ModelStrategy.Auto,
            new LlmPromptBuilder
            {
                Messages = [new LlmMessage("user", "Say hi")]
            },
            cancellationToken: TestContext.Current.CancellationToken);

        response.Content.Should().Be("from healthy");

        var stats = await memory.GetStatsAsync(
            "model-a:Ollama",
            TestContext.Current.CancellationToken);
        stats.AvailabilityFailures.Should().Be(1);
        stats.UnavailableUntil.Should().NotBeNull();
        stats.UnavailableUntil!.Value.Should().BeAfter(DateTimeOffset.UtcNow);

        router.Resolve(ModelStrategy.Auto)
            .Should().Be(new ResolvedEndpoint("model-b:Ollama", "model-b", ApiStyle.Ollama));

        response.RouterDiagnostics.Should().NotBeNull();
        response.RouterDiagnostics!.Attempts.Should().HaveCount(2);
        response.RouterDiagnostics.Attempts[0].Outcome
            .Should().Be(LlmRouterAttemptOutcome.Failed);
        response.RouterDiagnostics.Attempts[0].UnavailableUntil.Should().NotBeNull();
        response.RouterDiagnostics.Attempts[1].Outcome
            .Should().Be(LlmRouterAttemptOutcome.Succeeded);
    }

    [Fact]
    public async Task Router_DoesNotFallBackOnStatuslessNonFallbackableFailure()
    {
        var authenticationClient = new ThrowingClient(
            new LlmClientException(
                "Claude streaming error (authentication_error): bad key",
                LlmClientFailureKind.Authentication));
        var healthyClient = new StubClient("from healthy");
        var lookup = new LlmModelLookup(
            new Dictionary<string, Func<ILlmClient>>
            {
                ["model-a"] = () => authenticationClient,
                ["model-b"] = () => healthyClient
            },
            new Dictionary<(string Model, ApiStyle ApiStyle), Func<ILlmClient>>
            {
                [("model-a", ApiStyle.Ollama)] = () => authenticationClient,
                [("model-b", ApiStyle.Ollama)] = () => healthyClient
            });
        var memory = new InMemoryLlmRouterMemory();
        var router = new LlmRouter(
            lookup,
            new Dictionary<ModelStrategy, IReadOnlyList<string>>
            {
                [ModelStrategy.Auto] = ["model-a", "model-b"]
            },
            memory);

        var action = async () =>
            await router.CompleteStreamingAsync(
                ModelStrategy.Auto,
                new LlmPromptBuilder
                {
                    Messages = [new LlmMessage("user", "Say hi")]
                },
                cancellationToken: TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<LlmClientException>()
            .Where(exception =>
                exception.FailureKind == LlmClientFailureKind.Authentication);

        var healthyStats = await memory.GetStatsAsync(
            "model-b:Ollama",
            TestContext.Current.CancellationToken);
        healthyStats.TotalCalls.Should().Be(0);

        var authenticationStats = await memory.GetStatsAsync(
            "model-a:Ollama",
            TestContext.Current.CancellationToken);
        authenticationStats.AvailabilityFailures.Should().Be(0);
    }

    [Fact]
    public async Task Router_EmitsAttemptDiagnosticsOnSuccessfulStream()
    {
        var client = new StubClient("ok");
        var lookup = new LlmModelLookup(
            new Dictionary<string, Func<ILlmClient>>
            {
                ["m"] = () => client
            },
            new Dictionary<(string Model, ApiStyle ApiStyle), Func<ILlmClient>>
            {
                [("m", ApiStyle.Ollama)] = () => client
            });
        var router = new LlmRouter(
            lookup,
            new Dictionary<ModelStrategy, IReadOnlyList<string>>(),
            new InMemoryLlmRouterMemory());

        var response = await router.CompleteStreamingAsync(
            "m",
            new LlmPromptBuilder
            {
                Messages = [new LlmMessage("user", "Say hi")]
            },
            cancellationToken: TestContext.Current.CancellationToken);

        response.RouterDiagnostics.Should().NotBeNull();
        var attempt = response.RouterDiagnostics!.Attempts.Should()
            .ContainSingle().Subject;
        attempt.Outcome.Should().Be(LlmRouterAttemptOutcome.Succeeded);
        attempt.EndpointModel.Should().Be("m");
        attempt.EndpointApiStyle.Should().Be("Ollama");
    }

    [Fact]
    public async Task Router_ReasoningFlushedBeforeContentOnSuccessfulEndpoint()
    {
        var client = new ReasoningThenContentClient("think hard ", "the answer");
        var lookup = new LlmModelLookup(
            new Dictionary<string, Func<ILlmClient>>
            {
                ["m"] = () => client
            },
            new Dictionary<(string Model, ApiStyle ApiStyle), Func<ILlmClient>>
            {
                [("m", ApiStyle.Ollama)] = () => client
            });
        var router = new LlmRouter(
            lookup,
            new Dictionary<ModelStrategy, IReadOnlyList<string>>(),
            new InMemoryLlmRouterMemory());

        var response = await router.CompleteStreamingAsync(
            "m",
            new LlmPromptBuilder
            {
                Messages = [new LlmMessage("user", "Say hi")]
            },
            cancellationToken: TestContext.Current.CancellationToken);

        response.Reasoning.Should().Be("think hard ");
        response.Content.Should().Be("the answer");
    }

    [Fact]
    public async Task Router_FailoverDiscardsBufferedReasoningFromFailedEndpoint()
    {
        var reasoningThenFail = new ReasoningThenFailClient(
            "thoughts from a",
            new LlmClientException("a down", statusCode: 500));
        var healthyClient = new StubClient("answer from b");
        var lookup = new LlmModelLookup(
            new Dictionary<string, Func<ILlmClient>>
            {
                ["model-a"] = () => reasoningThenFail,
                ["model-b"] = () => healthyClient
            },
            new Dictionary<(string Model, ApiStyle ApiStyle), Func<ILlmClient>>
            {
                [("model-a", ApiStyle.Ollama)] = () => reasoningThenFail,
                [("model-b", ApiStyle.Ollama)] = () => healthyClient
            });
        var router = new LlmRouter(
            lookup,
            new Dictionary<ModelStrategy, IReadOnlyList<string>>
            {
                [ModelStrategy.Auto] = ["model-a", "model-b"]
            },
            new InMemoryLlmRouterMemory());

        var response = await router.CompleteStreamingAsync(
            ModelStrategy.Auto,
            new LlmPromptBuilder
            {
                Messages = [new LlmMessage("user", "Say hi")]
            },
            cancellationToken: TestContext.Current.CancellationToken);

        // The failed endpoint's reasoning must not leak into the successful
        // endpoint's answer.
        response.Reasoning.Should().BeNull();
        response.Content.Should().Be("answer from b");

        response.RouterDiagnostics.Should().NotBeNull();
        response.RouterDiagnostics!.Attempts.Should().HaveCount(2);
        response.RouterDiagnostics.Attempts[0].Outcome
            .Should().Be(LlmRouterAttemptOutcome.Failed);
        response.RouterDiagnostics.Attempts[1].Outcome
            .Should().Be(LlmRouterAttemptOutcome.Succeeded);
    }

    [Fact]
    public async Task Router_FailoverAfterReasoningAndContentKeepsBoth()
    {
        var reasoningAndContentThenFail = new ReasoningThenFailClient(
            "thoughts from a",
            new LlmClientException("a down", statusCode: 500),
            content: "partial answer");
        var healthyClient = new StubClient("answer from b");
        var lookup = new LlmModelLookup(
            new Dictionary<string, Func<ILlmClient>>
            {
                ["model-a"] = () => reasoningAndContentThenFail,
                ["model-b"] = () => healthyClient
            },
            new Dictionary<(string Model, ApiStyle ApiStyle), Func<ILlmClient>>
            {
                [("model-a", ApiStyle.Ollama)] = () => reasoningAndContentThenFail,
                [("model-b", ApiStyle.Ollama)] = () => healthyClient
            });
        var memory = new InMemoryLlmRouterMemory();
        var router = new LlmRouter(
            lookup,
            new Dictionary<ModelStrategy, IReadOnlyList<string>>
            {
                [ModelStrategy.Auto] = ["model-a", "model-b"]
            },
            memory);

        var action = async () => await router.CompleteStreamingAsync(
            ModelStrategy.Auto,
            new LlmPromptBuilder
            {
                Messages = [new LlmMessage("user", "Say hi")]
            },
            cancellationToken: TestContext.Current.CancellationToken);

        // Once content has been emitted the router must not fail over, so the
        // failure surfaces to the caller.
        await action.Should().ThrowAsync<LlmClientException>()
            .Where(exception => exception.StatusCode == 500);

        var healthyStats = await memory.GetStatsAsync(
            "model-b:Ollama",
            TestContext.Current.CancellationToken);
        healthyStats.TotalCalls.Should().Be(0);
    }

    [Fact]
    public async Task Router_FailoverDiscardsBufferedUsageAndReasoningFromFailedEndpoint()
    {
        var reasoningThenUsageThenFail = new ReasoningThenUsageFailClient(
            "thoughts from a",
            usage: new LlmUsage(
                PromptTokens: 5,
                CompletionTokens: 2,
                TotalTokens: 7));
        var healthyClient = new StubClient("answer from b");
        var lookup = new LlmModelLookup(
            new Dictionary<string, Func<ILlmClient>>
            {
                ["model-a"] = () => reasoningThenUsageThenFail,
                ["model-b"] = () => healthyClient
            },
            new Dictionary<(string Model, ApiStyle ApiStyle), Func<ILlmClient>>
            {
                [("model-a", ApiStyle.Ollama)] = () => reasoningThenUsageThenFail,
                [("model-b", ApiStyle.Ollama)] = () => healthyClient
            });
        var router = new LlmRouter(
            lookup,
            new Dictionary<ModelStrategy, IReadOnlyList<string>>
            {
                [ModelStrategy.Auto] = ["model-a", "model-b"]
            },
            new InMemoryLlmRouterMemory());

        var response = await router.CompleteStreamingAsync(
            ModelStrategy.Auto,
            new LlmPromptBuilder
            {
                Messages = [new LlmMessage("user", "Say hi")]
            },
            cancellationToken: TestContext.Current.CancellationToken);

        // Neither the failed endpoint's thoughts nor its usage may leak into
        // the successful endpoint's answer.
        response.Content.Should().Be("answer from b");
        response.Usage.Should().BeNull();
        response.Reasoning.Should().BeNull();

        response.RouterDiagnostics.Should().NotBeNull();
        response.RouterDiagnostics!.Attempts.Should().HaveCount(2);
        response.RouterDiagnostics.Attempts[0].Outcome
            .Should().Be(LlmRouterAttemptOutcome.Failed);
        response.RouterDiagnostics.Attempts[1].Outcome
            .Should().Be(LlmRouterAttemptOutcome.Succeeded);
    }

    [Fact]
    public async Task Router_ExhaustedEndpointsRethrowsLastFailure()
    {
        var first = new ThrowingClient(
            new LlmClientException("first down", statusCode: 500));
        var second = new ThrowingClient(
            new LlmClientException("second down", statusCode: 503));
        var lookup = new LlmModelLookup(
            new Dictionary<string, Func<ILlmClient>>
            {
                ["model-a"] = () => first,
                ["model-b"] = () => second
            },
            new Dictionary<(string Model, ApiStyle ApiStyle), Func<ILlmClient>>
            {
                [("model-a", ApiStyle.Ollama)] = () => first,
                [("model-b", ApiStyle.Ollama)] = () => second
            });
        var router = new LlmRouter(
            lookup,
            new Dictionary<ModelStrategy, IReadOnlyList<string>>
            {
                [ModelStrategy.Auto] = ["model-a", "model-b"]
            },
            new InMemoryLlmRouterMemory());

        var events = new List<LlmStreamEvent>();
        Exception? thrown = null;

        try
        {
            await foreach (var evt in router.StreamAsync(
                ModelStrategy.Auto,
                new LlmPromptBuilder
                {
                    Messages = [new LlmMessage("user", "Say hi")]
                },
                TestContext.Current.CancellationToken))
            {
                events.Add(evt);
            }
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        thrown.Should().BeOfType<LlmClientException>()
            .Which.StatusCode.Should().Be(503);

        events.Should().ContainSingle(evt => evt.RouterDiagnostics != null);
        var diagnostics = events.Last(evt => evt.RouterDiagnostics != null)
            .RouterDiagnostics!;
        diagnostics.Attempts.Should().HaveCount(2);
        diagnostics.Attempts.Should()
            .OnlyContain(a => a.Outcome == LlmRouterAttemptOutcome.Failed);
        diagnostics.Attempts[0].EndpointModel.Should().Be("model-a");
        diagnostics.Attempts[1].EndpointModel.Should().Be("model-b");
    }

    [Fact]
    public async Task Router_SharedDeadlineStopsFallback()
    {
        var lookup = new LlmModelLookup(
            new Dictionary<string, Func<ILlmClient>>
            {
                ["model-a"] = () => new SlowClient(),
                ["model-b"] = () => new StubClient("from healthy")
            },
            new Dictionary<(string Model, ApiStyle ApiStyle), Func<ILlmClient>>
            {
                [("model-a", ApiStyle.Ollama)] = () => new SlowClient(),
                [("model-b", ApiStyle.Ollama)] = () => new StubClient("from healthy")
            });
        var memory = new InMemoryLlmRouterMemory();
        var router = new LlmRouter(
            lookup,
            new Dictionary<ModelStrategy, IReadOnlyList<string>>
            {
                [ModelStrategy.Auto] = ["model-a", "model-b"]
            },
            memory,
            requestTimeout: TimeSpan.FromMilliseconds(50));

        var action = async () =>
            await router.CompleteStreamingAsync(
                ModelStrategy.Auto,
                new LlmPromptBuilder
                {
                    Messages = [new LlmMessage("user", "Say hi")]
                },
                cancellationToken: TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<TaskCanceledException>();

        var healthyStats = await memory.GetStatsAsync(
            "model-b:Ollama",
            TestContext.Current.CancellationToken);
        healthyStats.TotalCalls.Should().Be(0);

        var slowStats = await memory.GetStatsAsync(
            "model-a:Ollama",
            TestContext.Current.CancellationToken);
        slowStats.AvailabilityFailures.Should().Be(1);
    }

    [Fact]
    public async Task InMemoryRouterMemory_UnavailableUntil_KeepsLatestBlockedUntil()
    {
        var memory = new InMemoryLlmRouterMemory();

        await memory.RecordFailureAsync(
            "m:Ollama",
            LlmFailureCategory.Availability,
            unavailableUntil: DateTimeOffset.UtcNow.AddSeconds(5),
            cancellationToken: TestContext.Current.CancellationToken);
        await memory.RecordFailureAsync(
            "m:Ollama",
            LlmFailureCategory.Availability,
            unavailableUntil: DateTimeOffset.UtcNow.AddSeconds(30),
            cancellationToken: TestContext.Current.CancellationToken);

        var stats = await memory.GetStatsAsync("m:Ollama", TestContext.Current.CancellationToken);
        stats.UnavailableUntil!.Value
            .Should().BeAfter(DateTimeOffset.UtcNow.AddSeconds(25));
    }

    [Fact]
    public async Task LlmRouter_BoundsConcurrency()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new BlockingClient(gate.Task);
        var lookup = new LlmModelLookup(
            new Dictionary<string, Func<ILlmClient>>
            {
                ["m"] = () => client
            },
            new Dictionary<(string Model, ApiStyle ApiStyle), Func<ILlmClient>>
            {
                [("m", ApiStyle.Ollama)] = () => client
            });
        var memory = new InMemoryLlmRouterMemory();
        var router = new LlmRouter(
            lookup,
            new Dictionary<ModelStrategy, IReadOnlyList<string>>(),
            memory,
            maxPendingRequests: 1);

        var builder = new LlmPromptBuilder
        {
            Messages = [new LlmMessage("user", "hi")]
        };
        var first = router.CompleteStreamingAsync("m", builder, cancellationToken: TestContext.Current.CancellationToken);
        var second = router.CompleteStreamingAsync("m", builder, cancellationToken: TestContext.Current.CancellationToken);

        await Task.Delay(50, TestContext.Current.CancellationToken);
        second.IsCompleted.Should().BeFalse();

        gate.SetResult();

        var results = await Task.WhenAll(first, second);
        results.Should().OnlyContain(r => r.Content == "ok");

        var stats = await memory.GetStatsAsync("m:Ollama", TestContext.Current.CancellationToken);
        stats.TotalCalls.Should().Be(2);
    }

    [Fact]
    public async Task LlmRouter_TimesOutSlowStream()
    {
        var lookup = new LlmModelLookup(
            new Dictionary<string, Func<ILlmClient>>
            {
                ["m"] = () => new SlowClient()
            },
            new Dictionary<(string Model, ApiStyle ApiStyle), Func<ILlmClient>>
            {
                [("m", ApiStyle.Ollama)] = () => new SlowClient()
            });
        var memory = new InMemoryLlmRouterMemory();
        var router = new LlmRouter(
            lookup,
            new Dictionary<ModelStrategy, IReadOnlyList<string>>(),
            memory,
            requestTimeout: TimeSpan.FromMilliseconds(50));

        var action = async () => await router.CompleteStreamingAsync(
            "m",
            new LlmPromptBuilder
            {
                Messages = [new LlmMessage("user", "hi")]
            },
            cancellationToken: TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<TaskCanceledException>();

        var stats = await memory.GetStatsAsync("m:Ollama", TestContext.Current.CancellationToken);
        stats.AvailabilityFailures.Should().Be(1);
    }

    [Fact]
    public async Task EnvironmentSecretProvider_ReturnsEnvironmentVariable()
    {
        const string name = "PENGHOU_ROUTER_TEST_SECRET";

        try
        {
            Environment.SetEnvironmentVariable(name, "sk-test");
            var value = await new EnvironmentSecretProvider()
                .GetSecretAsync(name, TestContext.Current.CancellationToken);
            value.Should().Be("sk-test");
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public void BuildLookup_ResolvesSecretsThroughRegisteredProvider()
    {
        var stub = new RecordingSecretProvider("sk-stub");
        var sp = new StubServiceProvider(new Dictionary<Type, object>
        {
            [typeof(IHttpClientFactory)] = new TestHttpClientFactory(new HttpClient()),
            [typeof(ISecretProvider)] = stub
        });
        var options = new LlmRoutingOptions
        {
            Models =
            [
                new LlmModelOptions
                {
                    Name = "m",
                    Endpoints =
                    [
                        new LlmEndpointOptions
                        {
                            ApiStyle = ApiStyle.Ollama,
                            ApiKeyEnvVar = "TEST_LLM_KEY"
                        }
                    ]
                }
            ]
        };

        var lookup = ServiceCollectionExtensions.BuildLookup(sp, options);

        lookup.GetClient("m").Should().NotBeNull();
        stub.RequestedNames.Should().Contain("TEST_LLM_KEY");
    }

    [Fact]
    public void BuildLookup_ThrowsWhenRegisteredSecretIsMissing()
    {
        var sp = new StubServiceProvider(new Dictionary<Type, object>
        {
            [typeof(IHttpClientFactory)] = new TestHttpClientFactory(new HttpClient()),
            [typeof(ISecretProvider)] = new RecordingSecretProvider(null)
        });
        var options = new LlmRoutingOptions
        {
            Models =
            [
                new LlmModelOptions
                {
                    Name = "m",
                    Endpoints = [new LlmEndpointOptions { ApiStyle = ApiStyle.OpenAi, ApiKeyEnvVar = "MISSING_KEY" }]
                }
            ]
        };

        var action = () => ServiceCollectionExtensions.BuildLookup(sp, options);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*MISSING_KEY*");
    }

    [Fact]
    public void BuildLookup_SameStyleEndpoints_RegisterStyleAccessorFirstWins()
    {
        var sp = new StubServiceProvider(new Dictionary<Type, object>
        {
            [typeof(IHttpClientFactory)] = new TestHttpClientFactory(new HttpClient()),
            [typeof(ISecretProvider)] = new RecordingSecretProvider(null)
        });
        var options = new LlmRoutingOptions
        {
            Profiles =
                new Dictionary<string, LlmEndpointCapabilitiesOptions>
                {
                    ["first"] = new()
                    {
                        Thinking = true,
                        ThinkingBudget = 1024
                    },
                    ["second"] = new()
                    {
                        Thinking = true,
                        ThinkingBudget = 2048
                    }
                },
            Models =
            [
                new LlmModelOptions
                {
                    Name = "m",
                    Endpoints =
                    [
                        new LlmEndpointOptions
                        {
                            ApiStyle = ApiStyle.OpenAi,
                            Id = "gw-1",
                            Profile = "first"
                        },
                        new LlmEndpointOptions
                        {
                            ApiStyle = ApiStyle.OpenAi,
                            Id = "gw-2",
                            Profile = "second"
                        }
                    ]
                }
            ]
        };

        var lookup =
            ServiceCollectionExtensions.BuildLookup(sp, options);

        // The (model, API style) accessor must return the first registered
        // endpoint of the style, mirroring the plain-name default, instead of
        // silently pointing at the last registration.
        lookup.GetClient("m", ApiStyle.OpenAi)
            .Capabilities.ThinkingBudget.Should().Be(1024);
        lookup.GetClient("m")
            .Capabilities.ThinkingBudget.Should().Be(1024);

        // Both endpoints remain individually reachable by their id.
        lookup.GetClientByEndpointId("gw-1")
            .Capabilities.ThinkingBudget.Should().Be(1024);
        lookup.GetClientByEndpointId("gw-2")
            .Capabilities.ThinkingBudget.Should().Be(2048);
    }

    [Fact]
    public void DefaultCapabilities_AreConservativeForOllama()
    {
        var capabilities =
            Provider(ApiStyle.Ollama).DefaultCapabilities;

        capabilities.NativeToolCalling.Should().BeFalse();
        capabilities.ParallelToolCalls.Should().BeFalse();
        capabilities.NativeStructuredOutput.Should().BeFalse();
        capabilities.StructuredOutputViaTool.Should().BeFalse();
        capabilities.Thinking.Should().BeFalse();
        capabilities.ThinkingDisable.Should().BeFalse();
        capabilities.StreamingToolCallArguments.Should().BeFalse();
    }

    [Fact]
    public void DefaultCapabilities_AreConservativeForOpenAi()
    {
        var capabilities =
            Provider(ApiStyle.OpenAi).DefaultCapabilities;

        capabilities.NativeToolCalling.Should().BeTrue();
        capabilities.ParallelToolCalls.Should().BeFalse();
        capabilities.NativeStructuredOutput.Should().BeFalse();
        capabilities.Thinking.Should().BeFalse();
        capabilities.StreamingToolCallArguments.Should().BeTrue();
    }

    [Fact]
    public void DefaultCapabilities_ClaimWireLevelThinkingEfforts()
    {
        var openAi =
            Provider(ApiStyle.OpenAi).DefaultCapabilities;
        openAi.SupportedThinkingEfforts.Should()
            .BeEquivalentTo(
                new[]
                {
                    LlmThinkingEffort.Low,
                    LlmThinkingEffort.Medium,
                    LlmThinkingEffort.High
                });

        var claude =
            Provider(ApiStyle.Claude).DefaultCapabilities;
        claude.SupportedThinkingEfforts.Should()
            .BeEquivalentTo(
                new[]
                {
                    LlmThinkingEffort.Low,
                    LlmThinkingEffort.Medium,
                    LlmThinkingEffort.High
                });

        var gemini =
            Provider(ApiStyle.Gemini).DefaultCapabilities;
        gemini.SupportedThinkingEfforts.Should()
            .BeEquivalentTo(
                new[]
                {
                    LlmThinkingEffort.Low,
                    LlmThinkingEffort.Medium,
                    LlmThinkingEffort.High,
                    LlmThinkingEffort.Max
                });

        var ollama =
            Provider(ApiStyle.Ollama).DefaultCapabilities;
        ollama.SupportedThinkingEfforts.Should().BeEmpty();
    }

    [Fact]
    public void DefaultCapabilities_AdvertiseThinkingDisableOnlyWhereEncodeable()
    {
        var openAi =
            Provider(ApiStyle.OpenAi).DefaultCapabilities;
        var claude =
            Provider(ApiStyle.Claude).DefaultCapabilities;
        var gemini =
            Provider(ApiStyle.Gemini).DefaultCapabilities;
        var ollama =
            Provider(ApiStyle.Ollama).DefaultCapabilities;

        // The adapters encode an explicit disabled thinking mode on the wire
        // for Claude ({"type":"disabled"}) and Gemini ({"thinkingBudget":0}),
        // so their defaults may advertise it; OpenAI's toggle is dialect
        // specific (DeepSeek) and Ollama has no thinking at all.
        claude.ThinkingDisable.Should().BeTrue();
        gemini.ThinkingDisable.Should().BeTrue();
        openAi.ThinkingDisable.Should().BeFalse();
        ollama.ThinkingDisable.Should().BeFalse();
    }

    [Fact]
    public void ResolveCapabilities_AppliesProfileOverConservativeDefaults()
    {
        var profiles = new Dictionary<string, LlmEndpointCapabilitiesOptions>
        {
            ["tool-capable"] = new()
            {
                NativeToolCalling = true,
                ParallelToolCalls = true,
                NativeStructuredOutput = true,
                SupportedThinkingEfforts =
                    new List<LlmThinkingEffort>
                    {
                        LlmThinkingEffort.Max
                    },
                ThinkingBudget = 8192
            }
        };
        var endpoint = new LlmEndpointOptions
        {
            ApiStyle = ApiStyle.Ollama,
            Profile = "tool-capable"
        };

        var capabilities =
            ServiceCollectionExtensions.ResolveCapabilities(
                endpoint,
                profiles,
                Provider(ApiStyle.Ollama));

        capabilities.NativeToolCalling.Should().BeTrue();
        capabilities.ParallelToolCalls.Should().BeTrue();
        capabilities.NativeStructuredOutput.Should().BeTrue();
        capabilities.StreamingToolCallArguments.Should().BeFalse();
        capabilities.SupportedThinkingEfforts.Should()
            .BeEquivalentTo(
                new[] { LlmThinkingEffort.Max });
        capabilities.ThinkingBudget.Should().Be(8192);
    }

    [Fact]
    public void ResolveCapabilities_EndpointOverridesBeatProfile()
    {
        var profiles = new Dictionary<string, LlmEndpointCapabilitiesOptions>
        {
            ["tool-capable"] = new() { NativeToolCalling = true }
        };
        var endpoint = new LlmEndpointOptions
        {
            ApiStyle = ApiStyle.Ollama,
            Profile = "tool-capable",
            Capabilities = new LlmEndpointCapabilitiesOptions
            {
                NativeToolCalling = false
            }
        };

        var capabilities =
            ServiceCollectionExtensions.ResolveCapabilities(
                endpoint,
                profiles,
                Provider(ApiStyle.Ollama));

        capabilities.NativeToolCalling.Should().BeFalse();
    }

    [Fact]
    public void ResolveCapabilities_ThrowsForUnknownProfile()
    {
        var endpoint = new LlmEndpointOptions
        {
            ApiStyle = ApiStyle.Ollama,
            Profile = "missing"
        };

        var action = () =>
            ServiceCollectionExtensions.ResolveCapabilities(
                endpoint,
                new Dictionary<string, LlmEndpointCapabilitiesOptions>(),
                Provider(ApiStyle.Ollama));

        action.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*unknown capability profile 'missing'*");
    }

    [Fact]
    public void TryValidate_RejectsUnknownProfileReference()
    {
        var options = new LlmRoutingOptions
        {
            Models =
            [
                new LlmModelOptions
                {
                    Name = "m",
                    Endpoints =
                    [
                        new LlmEndpointOptions
                        {
                            ApiStyle = ApiStyle.Ollama,
                            Profile = "missing"
                        }
                    ]
                }
            ]
        };

        ServiceCollectionExtensions.TryValidate(
            options,
            out var error).Should().BeFalse();
        error.Should().Contain("unknown capability profile 'missing'");
    }

    [Fact]
    public async Task ReloadingLlmRouter_AppliesOptionsChange()
    {
        var monitor = new ManualOptionsMonitor(new LlmRoutingOptions
        {
            Models =
            [
                new LlmModelOptions { Name = "model-a", Endpoints = [new LlmEndpointOptions { ApiStyle = ApiStyle.Ollama }] }
            ],
            StrategyFallbacks = new Dictionary<ModelStrategy, List<string>>
            {
                [ModelStrategy.Auto] = ["model-a"]
            }
        });
        var sp = new StubServiceProvider(new Dictionary<Type, object>
        {
            [typeof(IHttpClientFactory)] = new TestHttpClientFactory(
                new HttpClient(new ModelEchoHandler())),
            [typeof(ISecretProvider)] = new RecordingSecretProvider(string.Empty)
        });
        var router = new ReloadingLlmRouter(monitor, sp, new InMemoryLlmRouterMemory());

        var builder = new LlmPromptBuilder
        {
            Messages = [new LlmMessage("user", "hi")]
        };

        var before = await router.CompleteStreamingAsync(
            ModelStrategy.Auto,
            builder,
            cancellationToken: TestContext.Current.CancellationToken);
        before.Content.Should().Be("from a");

        monitor.Set(new LlmRoutingOptions
        {
            Models =
            [
                new LlmModelOptions { Name = "model-b", Endpoints = [new LlmEndpointOptions { ApiStyle = ApiStyle.Ollama }] }
            ],
            StrategyFallbacks = new Dictionary<ModelStrategy, List<string>>
            {
                [ModelStrategy.Auto] = ["model-b"]
            }
        });

        var after = await router.CompleteStreamingAsync(
            ModelStrategy.Auto,
            builder,
            cancellationToken: TestContext.Current.CancellationToken);
        after.Content.Should().Be("from b");
    }

    [Fact]
    public async Task ReloadingLlmRouter_DisposeReleasesSubscription()
    {
        var monitor = new ManualOptionsMonitor(new LlmRoutingOptions
        {
            Models =
            [
                new LlmModelOptions { Name = "model-a", Endpoints = [new LlmEndpointOptions { ApiStyle = ApiStyle.Ollama }] }
            ],
            StrategyFallbacks = new Dictionary<ModelStrategy, List<string>>
            {
                [ModelStrategy.Auto] = ["model-a"]
            }
        });
        var sp = new StubServiceProvider(new Dictionary<Type, object>
        {
            [typeof(IHttpClientFactory)] = new TestHttpClientFactory(
                new HttpClient(new ModelEchoHandler())),
            [typeof(ISecretProvider)] = new RecordingSecretProvider(string.Empty)
        });
        var router = new ReloadingLlmRouter(monitor, sp, new InMemoryLlmRouterMemory());
        router.Dispose();

        monitor.Set(new LlmRoutingOptions
        {
            Models =
            [
                new LlmModelOptions { Name = "model-b", Endpoints = [new LlmEndpointOptions { ApiStyle = ApiStyle.Ollama }] }
            ],
            StrategyFallbacks = new Dictionary<ModelStrategy, List<string>>
            {
                [ModelStrategy.Auto] = ["model-b"]
            }
        });

        var response = await router.CompleteStreamingAsync(
            ModelStrategy.Auto,
            new LlmPromptBuilder
            {
                Messages = [new LlmMessage("user", "hi")]
            },
            cancellationToken: TestContext.Current.CancellationToken);

        response.Content.Should().Be("from a");
    }

    [Fact]
    public void ReloadingLlmModelLookup_AppliesOptionsChange()
    {
        var monitor = new ManualOptionsMonitor(new LlmRoutingOptions
        {
            Models =
            [
                new LlmModelOptions { Name = "model-a", Endpoints = [new LlmEndpointOptions { ApiStyle = ApiStyle.Ollama }] }
            ]
        });
        var sp = new StubServiceProvider(new Dictionary<Type, object>
        {
            [typeof(IHttpClientFactory)] = new TestHttpClientFactory(
                new HttpClient(new ModelEchoHandler())),
            [typeof(ISecretProvider)] = new RecordingSecretProvider(string.Empty)
        });
        using var lookup = new ReloadingLlmModelLookup(monitor, sp);

        lookup.GetClient("model-a").Should().NotBeNull();
        lookup.GetApiStyles("model-a").Should().Contain(ApiStyle.Ollama);

        monitor.Set(new LlmRoutingOptions
        {
            Models =
            [
                new LlmModelOptions { Name = "model-b", Endpoints = [new LlmEndpointOptions { ApiStyle = ApiStyle.Ollama }] }
            ]
        });

        lookup.GetApiStyles("model-a").Should().BeEmpty();
        lookup.GetClient("model-b").Should().NotBeNull();
        lookup.GetApiStyles("model-b").Should().Contain(ApiStyle.Ollama);
    }

    [Fact]
    public void ReloadingLlmModelLookup_DisposeReleasesSubscription()
    {
        var monitor = new ManualOptionsMonitor(new LlmRoutingOptions
        {
            Models =
            [
                new LlmModelOptions { Name = "model-a", Endpoints = [new LlmEndpointOptions { ApiStyle = ApiStyle.Ollama }] }
            ]
        });
        var sp = new StubServiceProvider(new Dictionary<Type, object>
        {
            [typeof(IHttpClientFactory)] = new TestHttpClientFactory(
                new HttpClient(new ModelEchoHandler())),
            [typeof(ISecretProvider)] = new RecordingSecretProvider(string.Empty)
        });
        var lookup = new ReloadingLlmModelLookup(monitor, sp);
        lookup.Dispose();

        monitor.Set(new LlmRoutingOptions
        {
            Models =
            [
                new LlmModelOptions { Name = "model-b", Endpoints = [new LlmEndpointOptions { ApiStyle = ApiStyle.Ollama }] }
            ]
        });

        lookup.GetApiStyles("model-a").Should().Contain(ApiStyle.Ollama);
        lookup.GetApiStyles("model-b").Should().BeEmpty();
    }

    [Fact]
    public void ConfigurationOptionsMonitor_BindsInitialValue()
    {
        var section = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LlmRouting:Models:0:Name"] = "m",
                ["LlmRouting:Models:0:Endpoints:0:ApiStyle"] = "Ollama"
            })
            .Build()
            .GetSection("LlmRouting");

        var monitor = new ConfigurationOptionsMonitor<LlmRoutingOptions>(
            section,
            (options, _) => ServiceCollectionExtensions.TryValidate(options, out _));

        monitor.CurrentValue.Models.Should().ContainSingle(m => m.Name == "m");
    }

    [Fact]
    public async Task ConfigurationOptionsMonitor_KeepsLastGoodOnInvalidReload()
    {
        var provider = new MutableConfigurationProvider();
        var root = new ConfigurationBuilder()
            .Add(new MutableConfigurationSource(provider))
            .Build();
        var section = root.GetSection("LlmRouting");

        var monitor = new ConfigurationOptionsMonitor<LlmRoutingOptions>(
            section,
            (options, _) => ServiceCollectionExtensions.TryValidate(options, out _));
        var changes = 0;
        using var subscription = monitor.OnChange((_, _) => changes++);

        provider.Update("LlmRouting:Models:0:Name", "m");
        provider.Update("LlmRouting:Models:0:Endpoints:0:ApiStyle", "Ollama");

        await Task.Delay(50, TestContext.Current.CancellationToken);
        changes.Should().Be(1);
        monitor.CurrentValue.Models.Should().ContainSingle(m => m.Name == "m");

        provider.Update("LlmRouting:Models:1:Name", "m");
        provider.Update("LlmRouting:Models:1:Endpoints:0:ApiStyle", "Claude");

        await Task.Delay(50, TestContext.Current.CancellationToken);
        changes.Should().Be(1);
        monitor.CurrentValue.Models.Should().ContainSingle(m => m.Name == "m");

        provider.Update("LlmRouting:Models:1:Name", "m2");

        await Task.Delay(50, TestContext.Current.CancellationToken);
        changes.Should().Be(2);
        monitor.CurrentValue.Models.Should().HaveCount(2);
    }

    private sealed class StubClient(string content) : ILlmClient
    {
        public LlmEndpointCapabilities Capabilities { get; } =
            new() { NativeToolCalling = true, ParallelToolCalls = true };

        public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
            LlmRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield return new LlmStreamEvent(Delta: content);
            yield return new LlmStreamEvent(FinishReason: "stop");
        }
    }

    private sealed class BlockingClient(Task gate) : ILlmClient
    {
        public LlmEndpointCapabilities Capabilities { get; } =
            new() { NativeToolCalling = true, ParallelToolCalls = true };

        public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
            LlmRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await gate;
            yield return new LlmStreamEvent(Delta: "ok");
            yield return new LlmStreamEvent(FinishReason: "stop");
        }
    }

    private sealed class SlowClient : ILlmClient
    {
        public LlmEndpointCapabilities Capabilities { get; } =
            new() { NativeToolCalling = true, ParallelToolCalls = true };

        public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
            LlmRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            yield return new LlmStreamEvent(Delta: "never");
        }
    }

    private sealed class ValidationThrowingClient(Exception exception) : ILlmClient
    {
        public LlmEndpointCapabilities Capabilities { get; } =
            new() { NativeToolCalling = true, ParallelToolCalls = true };

        public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
            LlmRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield return new LlmStreamEvent();
            throw exception;
        }
    }

    private sealed class EventClient(IReadOnlyList<LlmStreamEvent> events) : ILlmClient
    {
        public LlmEndpointCapabilities Capabilities { get; } =
            new() { NativeToolCalling = true, ParallelToolCalls = true };

        public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
            LlmRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;

            foreach (var evt in events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return evt;
            }
        }
    }

    private sealed class ThrowingClient(Exception exception) : ILlmClient
    {
        public LlmEndpointCapabilities Capabilities { get; } =
            new() { NativeToolCalling = true, ParallelToolCalls = true };

        public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
            LlmRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield return new LlmStreamEvent();
            throw exception;
        }
    }

    private sealed class EmitThenFailClient(string content, Exception exception) : ILlmClient
    {
        public LlmEndpointCapabilities Capabilities { get; } =
            new() { NativeToolCalling = true, ParallelToolCalls = true };

        public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
            LlmRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield return new LlmStreamEvent(Delta: content);
            throw exception;
        }
    }

    private sealed class ReasoningThenContentClient(string reasoning, string content) : ILlmClient
    {
        public LlmEndpointCapabilities Capabilities { get; } =
            new() { NativeToolCalling = true, ParallelToolCalls = true };

        public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
            LlmRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield return new LlmStreamEvent(ReasoningContent: reasoning);
            yield return new LlmStreamEvent(Delta: content);
            yield return new LlmStreamEvent(FinishReason: "stop");
        }
    }

    private sealed class ReasoningThenFailClient(
        string reasoning,
        Exception exception,
        string? content = null) : ILlmClient
    {
        public LlmEndpointCapabilities Capabilities { get; } =
            new() { NativeToolCalling = true, ParallelToolCalls = true };

        public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
            LlmRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield return new LlmStreamEvent(ReasoningContent: reasoning);

            if (content is not null)
                yield return new LlmStreamEvent(Delta: content);

            throw exception;
        }
    }

    private sealed class ReasoningThenUsageFailClient(
        string reasoning,
        LlmUsage usage) : ILlmClient
    {
        public LlmEndpointCapabilities Capabilities { get; } =
            new() { NativeToolCalling = true, ParallelToolCalls = true };

        public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
            LlmRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield return new LlmStreamEvent(ReasoningContent: reasoning);
            yield return new LlmStreamEvent(Usage: usage);
            throw new LlmClientException("a down", statusCode: 500);
        }
    }

    private sealed class ManualOptionsMonitor : IOptionsMonitor<LlmRoutingOptions>
    {
        private LlmRoutingOptions _value;
        private readonly List<Action<LlmRoutingOptions, string?>> _listeners = [];

        public ManualOptionsMonitor(LlmRoutingOptions value) => _value = value;

        public LlmRoutingOptions CurrentValue => _value;

        public LlmRoutingOptions Get(string? name) => _value;

        public IDisposable OnChange(Action<LlmRoutingOptions, string?> listener)
        {
            _listeners.Add(listener);
            return new DisposeAction(() => _listeners.Remove(listener));
        }

        public void Set(LlmRoutingOptions value)
        {
            _value = value;
            foreach (var listener in _listeners.ToArray())
                listener(value, Options.DefaultName);
        }

        private sealed class DisposeAction(Action dispose) : IDisposable
        {
            private Action? _dispose = dispose;

            public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
        }
    }

    private sealed class StubServiceProvider(Dictionary<Type, object> services) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            if (services.TryGetValue(serviceType, out var value))
                return value;

            return serviceType == typeof(ILlmClientProviderRegistry)
                ? BuiltInProviderRegistry
                : null;
        }
    }

    private static readonly ILlmClientProviderRegistry BuiltInProviderRegistry =
        new LlmClientProviderRegistry(
        [
            new OpenAiClientProvider(),
            new ClaudeClientProvider(),
            new GeminiClientProvider(),
            new OllamaClientProvider()
        ]);

    private static ILlmClientProvider Provider(ApiStyle apiStyle) =>
        BuiltInProviderRegistry.GetRequiredProvider(apiStyle.ToProviderKey());

    private sealed class TestHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingSecretProvider(string? value) : ISecretProvider
    {
        public List<string> RequestedNames { get; } = [];

        public Task<string?> GetSecretAsync(
            string name,
            CancellationToken cancellationToken = default)
        {
            RequestedNames.Add(name);
            return Task.FromResult(value);
        }
    }

    private sealed class MutableConfigurationProvider : ConfigurationProvider
    {
        public void Update(string key, string? value)
        {
            Data[key] = value;
            OnReload();
        }
    }

    private sealed class MutableConfigurationSource(IConfigurationProvider provider) : IConfigurationSource
    {
        public IConfigurationProvider Build(IConfigurationBuilder builder) => provider;
    }

    private sealed class ModelEchoHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var model = JsonSerializer.Deserialize<JsonElement>(body)
                .GetProperty("model")
                .GetString();
            var content = model == "model-a" ? "from a" : "from b";
            var responseBody =
                "{\"message\":{\"role\":\"assistant\",\"content\":\"" + content + "\"},\"done\":false}\n" +
                "{\"message\":{\"role\":\"assistant\",\"content\":\"\"},\"done\":true,\"done_reason\":\"stop\"}\n";

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
