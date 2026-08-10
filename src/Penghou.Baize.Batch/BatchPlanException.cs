namespace Penghou.Baize.Batch;

/// <summary>
/// Thrown when a logical batch cannot be planned: an unknown model or provider,
/// an unknown endpoint, an empty submission, or a request that routes to an
/// endpoint without native batch support.
/// </summary>
public sealed class BatchPlanException : Exception
{
    /// <summary>Initializes a new instance with a message.</summary>
    /// <param name="message">The planning failure description.</param>
    public BatchPlanException(string message) : base(message)
    {
    }
}
