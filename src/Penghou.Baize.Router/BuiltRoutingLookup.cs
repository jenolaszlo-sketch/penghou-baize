namespace Penghou.Baize.Router;

internal sealed record BuiltRoutingLookup(
    ILlmModelLookup Lookup,
    IReadOnlyList<DeferredEndpointRuntime> Endpoints);

internal sealed record DeferredEndpointRuntime(
    string EndpointId,
    string Provider,
    string Model,
    DeferredEndpointClients Clients,
    bool HasNativeBatch);
