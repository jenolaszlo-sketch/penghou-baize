namespace Penghou.Baize.Tools;

/// <summary>
/// The outcome of parsing a tool call into a strongly typed result: either a
/// successful <see cref="Value"/> or a <see cref="ToolCallParseFailure"/> with
/// diagnostic detail.
/// </summary>
/// <typeparam name="TResult">The deserialized result type.</typeparam>
public sealed record ToolCallParseResult<TResult>
{
    private ToolCallParseResult(
        bool succeeded,
        TResult? value,
        ToolCallParseFailure failure,
        string? error,
        string? raw)
    {
        Succeeded = succeeded;
        Value = value;
        Failure = failure;
        Error = error;
        Raw = raw;
    }

    /// <summary>
    /// Gets a value indicating whether the parse succeeded.
    /// </summary>
    public bool Succeeded { get; }

    /// <summary>
    /// Gets the deserialized result value when <see cref="Succeeded"/> is
    /// true; otherwise <c>default</c>.
    /// </summary>
    public TResult? Value { get; }

    /// <summary>
    /// Gets the failure reason when <see cref="Succeeded"/> is false;
    /// otherwise <see cref="ToolCallParseFailure.None"/>.
    /// </summary>
    public ToolCallParseFailure Failure { get; }

    /// <summary>
    /// Gets a human-readable diagnostic message for failed parses.
    /// </summary>
    public string? Error { get; }

    /// <summary>
    /// Gets the raw arguments JSON the parse was attempted against.
    /// </summary>
    public string? Raw { get; }

    /// <summary>
    /// Creates a successful result carrying <paramref name="value"/>.
    /// </summary>
    /// <param name="value">The deserialized result value.</param>
    /// <returns>A successful <see cref="ToolCallParseResult{TResult}"/>.</returns>
    public static ToolCallParseResult<TResult> Success(TResult value)
    {
        return new ToolCallParseResult<TResult>(
            succeeded: true,
            value: value,
            failure: ToolCallParseFailure.None,
            error: null,
            raw: null);
    }

    /// <summary>
    /// Creates a failed result with a failure reason and diagnostic detail.
    /// </summary>
    /// <param name="failure">The failure reason; must not be <see cref="ToolCallParseFailure.None"/>.</param>
    /// <param name="error">A human-readable diagnostic message.</param>
    /// <param name="raw">The raw arguments JSON that failed to parse.</param>
    /// <returns>A failed <see cref="ToolCallParseResult{TResult}"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="failure"/> is <see cref="ToolCallParseFailure.None"/>.</exception>
    public static ToolCallParseResult<TResult> Failed(
        ToolCallParseFailure failure,
        string error,
        string? raw)
    {
        if (failure == ToolCallParseFailure.None)
            throw new ArgumentOutOfRangeException(nameof(failure));

        return new ToolCallParseResult<TResult>(
            succeeded: false,
            value: default,
            failure: failure,
            error: error,
            raw: raw);
    }
}
