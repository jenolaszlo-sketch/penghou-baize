using Penghou.Baize.Batch;
using Penghou.Baize.Router;
using FluentAssertions;

namespace Penghou.Baize.Batch.Tests;

public sealed class BatchPlannerTests
{
    [Fact]
    public void Plan_SingleProviderRequests_GroupIntoSinglePhysicalBatch()
    {
        var (lookup, resolver) = Build(("gpt", ApiStyle.OpenAi, BatchCapabilities.NativeBatch | BatchCapabilities.Polling));
        var planner = new BatchPlanner(lookup, resolver);
        var submission = new BaizeBatchSubmission(
        [
            Request("1", "gpt", "one"),
            Request("2", "gpt", "two")
        ]);

        var plan = planner.Plan(submission);

        plan.Groups.Should().HaveCount(1);
        var group = plan.Groups[0];
        group.EndpointId.Should().Be("gpt:OpenAi");
        group.ProviderId.Should().Be("OpenAi");
        group.Items.Should().HaveCount(2);
        group.Items[0].RequestId.Should().Be("1");
        group.Items[1].RequestId.Should().Be("2");
    }

    [Fact]
    public void Plan_MixedProviderRequests_CreateSeparateGroupsPerProvider()
    {
        var (lookup, resolver) = Build(
            ("gpt", ApiStyle.OpenAi, BatchCapabilities.NativeBatch),
            ("claude", ApiStyle.Claude, BatchCapabilities.NativeBatch),
            ("gemini", ApiStyle.Gemini, BatchCapabilities.NativeBatch));
        var planner = new BatchPlanner(lookup, resolver);
        var submission = new BaizeBatchSubmission(
        [
            Request("1", "gpt", "a"),
            Request("2", "claude", "b"),
            Request("3", "gemini", "c"),
            Request("4", "gpt", "d")
        ]);

        var plan = planner.Plan(submission);

        plan.Groups.Should().HaveCount(3);
        plan.Groups.Select(g => g.ProviderId)
            .Should().Equal("OpenAi", "Claude", "Gemini");
        plan.Groups.Single(g => g.ProviderId == "OpenAi")
            .Items.Select(i => i.RequestId).Should().Equal("1", "4");
        plan.Groups.Single(g => g.ProviderId == "Claude")
            .Items.Select(i => i.RequestId).Should().Equal("2");
        plan.Groups.Single(g => g.ProviderId == "Gemini")
            .Items.Select(i => i.RequestId).Should().Equal("3");
    }

    [Fact]
    public void Plan_ResolvesExplicitProviderForSharedModelName()
    {
        var (lookup, resolver) = Build(
            ("gpt", ApiStyle.OpenAi, BatchCapabilities.NativeBatch),
            ("gpt", ApiStyle.Gemini, BatchCapabilities.NativeBatch));
        var planner = new BatchPlanner(lookup, resolver);
        var submission = new BaizeBatchSubmission(
        [
            new BaizeBatchRequest(
                "1",
                Req("via gemini"),
                Model: "gpt",
                Provider: "Gemini")
        ]);

        var plan = planner.Plan(submission);

        plan.Groups.Should().HaveCount(1);
        plan.Groups[0].ProviderId.Should().Be("Gemini");
        plan.Groups[0].EndpointId.Should().Be("gpt:Gemini");
    }

    [Fact]
    public void Plan_ResolvesExplicitEndpointId()
    {
        var (lookup, resolver) = Build(
            ("gpt", ApiStyle.OpenAi, BatchCapabilities.NativeBatch),
            ("claude", ApiStyle.Claude, BatchCapabilities.NativeBatch));
        var planner = new BatchPlanner(lookup, resolver);
        var submission = new BaizeBatchSubmission(
        [
            new BaizeBatchRequest(
                "1",
                Req("hello"),
                EndpointId: "claude:Claude")
        ]);

        var plan = planner.Plan(submission);

        plan.Groups.Should().HaveCount(1);
        plan.Groups[0].EndpointId.Should().Be("claude:Claude");
        plan.Groups[0].ProviderId.Should().Be("Claude");
        plan.Groups[0].Model.Should().BeNull();
    }

    [Fact]
    public void Plan_UnknownModel_Throws()
    {
        var (lookup, resolver) = Build(("gpt", ApiStyle.OpenAi, BatchCapabilities.NativeBatch));
        var planner = new BatchPlanner(lookup, resolver);

        var action = () => planner.Plan(new BaizeBatchSubmission(
        [
            Request("1", "missing", "hello")
        ]));

        action.Should().Throw<BatchPlanException>()
            .WithMessage("*unknown model 'missing'*");
    }

    [Fact]
    public void Plan_ModelWithoutRequestedProvider_Throws()
    {
        var (lookup, resolver) = Build(("gpt", ApiStyle.OpenAi, BatchCapabilities.NativeBatch));
        var planner = new BatchPlanner(lookup, resolver);

        var action = () => planner.Plan(new BaizeBatchSubmission(
        [
            new BaizeBatchRequest(
                "1",
                Req("hello"),
                Model: "gpt",
                Provider: "Claude")
        ]));

        action.Should().Throw<BatchPlanException>()
            .WithMessage("*no endpoint for provider 'Claude'*");
    }

    [Fact]
    public void Plan_EndpointWithoutNativeBatch_Throws()
    {
        var (lookup, resolver) = Build(("gpt", ApiStyle.OpenAi, BatchCapabilities.None));
        var planner = new BatchPlanner(lookup, resolver);

        var action = () => planner.Plan(new BaizeBatchSubmission(
        [
            Request("1", "gpt", "hello")
        ]));

        action.Should().Throw<BatchPlanException>()
            .WithMessage("*does not support native batching*");
    }

    [Fact]
    public void Plan_EmptySubmission_Throws()
    {
        var (lookup, resolver) = Build(("gpt", ApiStyle.OpenAi, BatchCapabilities.NativeBatch));
        var planner = new BatchPlanner(lookup, resolver);

        var action = () => planner.Plan(new BaizeBatchSubmission([]));

        action.Should().Throw<BatchPlanException>()
            .WithMessage("*contains no requests*");
    }

    [Fact]
    public void Plan_SplitsGroupsOverMaxItemsPerGroup()
    {
        var (lookup, resolver) = Build(("gpt", ApiStyle.OpenAi, BatchCapabilities.NativeBatch));
        var planner = new BatchPlanner(
            lookup,
            resolver,
            new BatchPlannerOptions { MaxItemsPerGroup = 2 });
        var submission = new BaizeBatchSubmission(
        [
            Request("1", "gpt", "a"),
            Request("2", "gpt", "b"),
            Request("3", "gpt", "c"),
            Request("4", "gpt", "d"),
            Request("5", "gpt", "e")
        ]);

        var plan = planner.Plan(submission);

        plan.Groups.Should().HaveCount(3);
        plan.Groups[0].Items.Select(i => i.RequestId).Should().Equal("1", "2");
        plan.Groups[1].Items.Select(i => i.RequestId).Should().Equal("3", "4");
        plan.Groups[2].Items.Select(i => i.RequestId).Should().Equal("5");
    }

    [Fact]
    public void Plan_PreservesSubmissionIdWhenProvided()
    {
        var (lookup, resolver) = Build(("gpt", ApiStyle.OpenAi, BatchCapabilities.NativeBatch));
        var planner = new BatchPlanner(lookup, resolver);

        var plan = planner.Plan(new BaizeBatchSubmission(
            [Request("1", "gpt", "a")],
            Id: "logical-123"));

        plan.LogicalBatchId.Should().Be("logical-123");
    }

    [Fact]
    public void Plan_GeneratesLogicalBatchIdWhenAbsent()
    {
        var (lookup, resolver) = Build(("gpt", ApiStyle.OpenAi, BatchCapabilities.NativeBatch));
        var planner = new BatchPlanner(lookup, resolver);

        var plan = planner.Plan(new BaizeBatchSubmission(
        [
            Request("1", "gpt", "a")
        ]));

        plan.LogicalBatchId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void BaizeBatchRequest_Create_PreservesColonInModelName()
    {
        var request = BaizeBatchRequest.Create(
            "1",
            "anthropic:claude-x",
            Req("hello"));

        request.Model.Should().Be("anthropic:claude-x");
        request.Provider.Should().BeNull();
        request.EndpointId.Should().BeNull();
    }

    [Fact]
    public void BaizeBatchRequest_CreateForProvider_UsesExplicitFields()
    {
        var request = BaizeBatchRequest.CreateForProvider(
            "1",
            "anthropic",
            "claude-x",
            Req("hello"));

        request.Model.Should().Be("claude-x");
        request.Provider.Should().Be("anthropic");
    }

    [Fact]
    public void Plan_DuplicateRequestIds_ThrowsBeforeRouting()
    {
        var (lookup, resolver) = Build(("gpt", ApiStyle.OpenAi, BatchCapabilities.NativeBatch));
        var planner = new BatchPlanner(lookup, resolver);

        var action = () => planner.Plan(new BaizeBatchSubmission(
        [
            Request("same", "gpt", "one"),
            Request("same", "gpt", "two")
        ]));

        action.Should().Throw<BatchPlanException>()
            .WithMessage("*duplicate request id 'same'*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_NonPositiveMaxItems_Throws(int maximum)
    {
        var (lookup, resolver) = Build(("gpt", ApiStyle.OpenAi, BatchCapabilities.NativeBatch));

        var action = () => new BatchPlanner(
            lookup,
            resolver,
            new BatchPlannerOptions { MaxItemsPerGroup = maximum });

        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void BaizeBatchRequest_Create_ParsesBareModelName()
    {
        var request = BaizeBatchRequest.Create(
            "1",
            "gpt-x",
            Req("hello"));

        request.Model.Should().Be("gpt-x");
        request.Provider.Should().BeNull();
    }

    private static BaizeBatchRequest Request(string id, string model, string prompt) =>
        new(id, Req(prompt), Model: model);

    private static LlmRequest Req(string prompt) =>
        new([new LlmMessage("user", prompt)]);

    private static (ILlmModelLookup Lookup, IBaizeBatchClientResolver Resolver) Build(
        params (string Model, ApiStyle Style, BatchCapabilities Batch)[] endpoints)
    {
        var chat = new StubChatClient();
        var defaults = new Dictionary<string, Func<ILlmClient>>();
        var byStyle = new Dictionary<(string, ApiStyle), Func<ILlmClient>>();
        var byEndpointId = new Dictionary<string, Func<ILlmClient>>();
        var byBatch = new Dictionary<string, Func<IBaizeBatchClient>>();
        var endpointsByModel =
            new Dictionary<string, IReadOnlyList<ResolvedEndpoint>>();

        foreach (var (model, style, batch) in endpoints)
        {
            defaults.TryAdd(model, () => chat);
            byStyle[(model, style)] = () => chat;
            var endpointId = $"{model}:{style}";
            byEndpointId[endpointId] = () => chat;
            byBatch[endpointId] =
                () => new StubBatchClient(style.ToString(), batch);

            if (!endpointsByModel.TryGetValue(model, out var list))
            {
                list = new List<ResolvedEndpoint>();
                endpointsByModel[model] = list;
            }

            ((List<ResolvedEndpoint>)list).Add(
                new ResolvedEndpoint(endpointId, model, style));
        }

        var lookup = new LlmModelLookup(
            defaults,
            byStyle,
            byEndpointId: byEndpointId,
            endpointsByModel: endpointsByModel);
        var resolver = new BatchClientResolver(byBatch);
        return (lookup, resolver);
    }

    private sealed class StubChatClient : ILlmClient
    {
        public LlmEndpointCapabilities Capabilities { get; } = new();

        public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
            LlmRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class StubBatchClient(
        string providerId,
        BatchCapabilities capabilities) : IBaizeBatchClient
    {
        public string ProviderId { get; } = providerId;
        public BatchCapabilities Capabilities { get; } = capabilities;

        public Task<ProviderBatchHandle> SubmitAsync(
            IReadOnlyList<BaizeBatchItem> items,
            BatchSubmissionOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                new ProviderBatchHandle(ProviderId, "batch-id"));

        public Task<ProviderBatchStatus> GetStatusAsync(
            ProviderBatchHandle handle,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                new ProviderBatchStatus(BaizeBatchState.Pending));

        public Task<IReadOnlyList<BaizeBatchResult>> GetResultsAsync(
            ProviderBatchHandle handle,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BaizeBatchResult>>([]);

        public Task CancelAsync(
            ProviderBatchHandle handle,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
