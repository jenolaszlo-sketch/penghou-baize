namespace Penghou.Baize;

/// <summary>
/// Thrown when a request asks an endpoint to do something its declared
/// capabilities do not allow, before the request is transmitted.
/// </summary>
public sealed class LlmRequestValidationException : Exception
{
    /// <summary>Initializes a request-validation exception.</summary>
    /// <param name="message">A description of the unsupported feature.</param>
    public LlmRequestValidationException(string message)
        : base(message)
    {
    }
}
