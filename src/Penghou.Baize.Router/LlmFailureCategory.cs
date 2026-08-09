namespace Penghou.Baize.Router;

/// <summary>
/// The categories of failure the router memory tracks for an endpoint.
/// </summary>
public enum LlmFailureCategory
{
    /// <summary>
    /// The endpoint was unreachable or returned a server-side error
    /// (connection failure, timeout, HTTP 5xx).
    /// </summary>
    Availability,

    /// <summary>
    /// A tool call produced by the endpoint required repair or
    /// normalization before it could be executed.
    /// </summary>
    ToolRepairNeeded,

    /// <summary>
    /// The endpoint's output did not conform to the requested
    /// structured-output schema.
    /// </summary>
    StructuredOutputMismatch
}
