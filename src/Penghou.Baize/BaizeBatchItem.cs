namespace Penghou.Baize;

/// <summary>
/// One logical request inside a physical provider batch, paired with the stable
/// caller-supplied identifier used to correlate the eventual result. The
/// provider batch client maps <see cref="RequestId"/> to its native correlation
/// mechanism (for example OpenAI or Anthropic <c>custom_id</c>, or Gemini's
/// JSONL <c>key</c>).
/// </summary>
/// <param name="RequestId">The stable identifier correlating the result back to the original request.</param>
/// <param name="Request">The canonical request to execute.</param>
public sealed record BaizeBatchItem(
    string RequestId,
    LlmRequest Request);
