namespace Penghou.Baize.Batch;

/// <summary>
/// A single logical request in a Baize batch. The same syntax supports both
/// single-provider and mixed-provider batches: each request resolves to an
/// endpoint through <see cref="EndpointId"/>, <see cref="Model"/> and/or
/// <see cref="Provider"/>.
/// </summary>
/// <param name="Id">The stable caller-supplied identifier correlating the eventual result.</param>
/// <param name="Request">The canonical request to execute.</param>
/// <param name="Model">The registered model name, when resolving through the model lookup.</param>
/// <param name="Provider">The provider key disambiguating which endpoint of the model to use, when any.</param>
/// <param name="EndpointId">An explicit endpoint id bypassing model resolution, when any.</param>
public sealed record BaizeBatchRequest(
    string Id,
    LlmRequest Request,
    string? Model = null,
    string? Provider = null,
    string? EndpointId = null)
{
    /// <summary>
    /// Creates a request for a model resolved through its configured default
    /// route. The model name is preserved verbatim, including any colons.
    /// </summary>
    /// <param name="id">The stable caller-supplied identifier.</param>
    /// <param name="model">The registered model name.</param>
    /// <param name="request">The canonical request to execute.</param>
    /// <returns>The logical batch request.</returns>
    public static BaizeBatchRequest Create(
        string id,
        string model,
        LlmRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        return new BaizeBatchRequest(id, request, Model: model);
    }

    /// <summary>Creates a request for an explicit provider and model.</summary>
    public static BaizeBatchRequest CreateForProvider(
        string id,
        string provider,
        string model,
        LlmRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        return new BaizeBatchRequest(id, request, Model: model, Provider: provider);
    }
}
