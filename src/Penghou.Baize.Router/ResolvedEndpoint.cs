namespace Penghou.Baize.Router;

/// <summary>
/// A concrete reachable target for a logical model. The endpoint id uniquely
/// identifies the specific endpoint (distinguishing two OpenAI-compatible
/// gateways for the same model, for example), so routing memory and cooldowns
/// are keyed by id rather than by the (model, API style) pair.
/// </summary>
/// <param name="EndpointId">The endpoint's stable unique identifier.</param>
/// <param name="Model">The logical model's registration name.</param>
/// <param name="Provider">The extensible provider adapter used to reach the model.</param>
public readonly record struct ResolvedEndpoint(
    string EndpointId,
    string Model,
    LlmProviderKey Provider)
{
    /// <summary>Initializes an endpoint for a legacy built-in API style.</summary>
    public ResolvedEndpoint(string endpointId, string model, ApiStyle apiStyle)
        : this(endpointId, model, apiStyle.ToProviderKey())
    {
    }

    /// <summary>
    /// The built-in API style.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this endpoint uses a third-party provider key. Use
    /// <see cref="Provider"/> for extensible provider code.
    /// </exception>
    public ApiStyle ApiStyle =>
        Provider.TryGetApiStyle(out var apiStyle)
            ? apiStyle
            : throw new InvalidOperationException(
                $"Provider '{Provider}' is not a built-in API style.");

    /// <summary>Tries to resolve this endpoint to a legacy built-in API style.</summary>
    public bool TryGetApiStyle(out ApiStyle apiStyle) =>
        Provider.TryGetApiStyle(out apiStyle);

    /// <summary>Deconstructs an endpoint using the legacy built-in API style.</summary>
    public void Deconstruct(
        out string endpointId,
        out string model,
        out ApiStyle apiStyle)
    {
        endpointId = EndpointId;
        model = Model;
        apiStyle = ApiStyle;
    }
}
