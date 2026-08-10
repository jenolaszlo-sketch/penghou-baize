namespace Penghou.Baize;

/// <summary>
/// The normalized result of one logical request in an asynchronous batch.
/// Results are correlated by <see cref="RequestId"/> and need not be returned in
/// submission order.
/// </summary>
/// <param name="RequestId">The stable identifier of the original request.</param>
/// <param name="State">The item outcome.</param>
/// <param name="Response">The normalized completion response, when the request succeeded.</param>
/// <param name="Error">The normalized failure, when the request failed.</param>
public sealed record BaizeBatchResult(
    string RequestId,
    BaizeBatchItemState State,
    LlmResponse? Response = null,
    BaizeError? Error = null);
