namespace Penghou.Baize.Router;

/// <summary>
/// A concrete reachable target for a logical model. The endpoint id uniquely
/// identifies the specific endpoint (distinguishing two OpenAI-compatible
/// gateways for the same model, for example), so routing memory and cooldowns
/// are keyed by id rather than by the (model, API style) pair.
/// </summary>
/// <param name="EndpointId">The endpoint's stable unique identifier.</param>
/// <param name="Model">The logical model's registration name.</param>
/// <param name="ApiStyle">The wire protocol used to reach the model.</param>
public readonly record struct ResolvedEndpoint(
    string EndpointId,
    string Model,
    ApiStyle ApiStyle);
