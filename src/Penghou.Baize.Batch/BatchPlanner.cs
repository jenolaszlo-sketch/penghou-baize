using Penghou.Baize.Router;

namespace Penghou.Baize.Batch;

/// <summary>
/// Default <see cref="IBaizeBatchPlanner"/> that routes each logical request to
/// a configured endpoint, groups compatible requests per provider, and splits
/// groups according to <see cref="BatchPlannerOptions"/>.
/// </summary>
public sealed class BatchPlanner : IBaizeBatchPlanner
{
    private readonly ILlmModelLookup _lookup;
    private readonly IBaizeBatchClientResolver _resolver;
    private readonly BatchPlannerOptions _options;

    /// <summary>Initializes a planner.</summary>
    /// <param name="lookup">The model lookup used to resolve request routes.</param>
    /// <param name="resolver">The endpoint-keyed batch client resolver.</param>
    /// <param name="options">Grouping limits, when any.</param>
    public BatchPlanner(
        ILlmModelLookup lookup,
        IBaizeBatchClientResolver resolver,
        BatchPlannerOptions? options = null)
    {
        _lookup = lookup;
        _resolver = resolver;
        _options = options ?? new BatchPlannerOptions();

        if (_options.MaxItemsPerGroup is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                _options.MaxItemsPerGroup,
                "MaxItemsPerGroup must be greater than zero when specified.");
        }
    }

    /// <inheritdoc />
    public BatchPlan Plan(BaizeBatchSubmission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);

        if (submission.Requests.Count == 0)
        {
            throw new BatchPlanException(
                "Batch submission contains no requests.");
        }

        var requestIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var request in submission.Requests)
        {
            if (string.IsNullOrWhiteSpace(request.Id))
            {
                throw new BatchPlanException(
                    "Every batch request must have a non-empty id.");
            }

            if (!requestIds.Add(request.Id))
            {
                throw new BatchPlanException(
                    $"Batch submission contains duplicate request id '{request.Id}'.");
            }
        }

        var logicalBatchId =
            string.IsNullOrWhiteSpace(submission.Id)
                ? Guid.NewGuid().ToString("N")
                : submission.Id!;

        var groups = new List<GroupBuilder>();
        var groupByKey = new Dictionary<
            (string EndpointId, string ProviderId),
            GroupBuilder>();

        foreach (var request in submission.Requests)
        {
            var (endpointId, providerId, model) =
                ResolveTarget(request);

            var client = _resolver.GetClient(endpointId);

            if (!client.Capabilities.HasFlag(BatchCapabilities.NativeBatch))
            {
                throw new BatchPlanException(
                    $"Request '{request.Id}' routes to endpoint '{endpointId}' " +
                    "which does not support native batching.");
            }

            if (!groupByKey.TryGetValue((endpointId, providerId), out var group))
            {
                group = new GroupBuilder(endpointId, providerId, model);
                groupByKey[(endpointId, providerId)] = group;
                groups.Add(group);
            }

            AppendItem(group, groups, groupByKey, request);
        }

        return new BatchPlan(
            logicalBatchId,
            groups.Select(group => group.Build()).ToArray());
    }

    private (string EndpointId, string ProviderId, string? Model) ResolveTarget(
        BaizeBatchRequest request)
    {
        if (request.EndpointId is not null)
        {
            if (!_resolver.TryGetClient(request.EndpointId, out var endpointClient))
            {
                throw new BatchPlanException(
                    $"Request '{request.Id}' references unknown endpoint id " +
                    $"'{request.EndpointId}'.");
            }

            return (request.EndpointId, endpointClient.ProviderId, request.Model);
        }

        if (string.IsNullOrWhiteSpace(request.Model))
        {
            throw new BatchPlanException(
                $"Request '{request.Id}' does not specify a model or endpoint.");
        }

        var endpoints = _lookup.GetEndpoints(request.Model!);

        if (endpoints.Count == 0)
        {
            throw new BatchPlanException(
                $"Request '{request.Id}' references unknown model " +
                $"'{request.Model}'.");
        }

        ResolvedEndpoint selected;

        if (!string.IsNullOrWhiteSpace(request.Provider))
        {
            var provider = new LlmProviderKey(request.Provider!);
            ResolvedEndpoint? match = endpoints
                .Where(endpoint => endpoint.Provider == provider)
                .Cast<ResolvedEndpoint?>()
                .FirstOrDefault();

            if (match is null)
            {
                throw new BatchPlanException(
                    $"Request '{request.Id}' model '{request.Model}' has no " +
                    $"endpoint for provider '{request.Provider}'.");
            }

            selected = match.Value;
        }
        else
        {
            // Default route: the model's first registered endpoint. Live
            // failover state is intentionally ignored so planning stays
            // deterministic and replayable.
            selected = endpoints[0];
        }

        return (selected.EndpointId, selected.Provider.Value, request.Model);
    }

    private void AppendItem(
        GroupBuilder group,
        List<GroupBuilder> groups,
        Dictionary<(string EndpointId, string ProviderId), GroupBuilder> groupByKey,
        BaizeBatchRequest request)
    {
        var max = _options.MaxItemsPerGroup;

        if (max is null || group.Items.Count < max.Value)
        {
            group.Items.Add(new BaizeBatchItem(request.Id, request.Request));
            return;
        }

        var split = new GroupBuilder(
            group.EndpointId,
            group.ProviderId,
            group.Model);
        split.Items.Add(new BaizeBatchItem(request.Id, request.Request));
        groupByKey[(group.EndpointId, group.ProviderId)] = split;
        groups.Add(split);
    }

    private sealed class GroupBuilder(
        string endpointId,
        string providerId,
        string? model)
    {
        public string EndpointId { get; } = endpointId;
        public string ProviderId { get; } = providerId;
        public string? Model { get; } = model;
        public List<BaizeBatchItem> Items { get; } = [];

        public ProviderBatchGroup Build() =>
            new(EndpointId, ProviderId, Model, Items.ToArray());
    }
}
