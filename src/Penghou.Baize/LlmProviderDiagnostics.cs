namespace Penghou.Baize;

/// <summary>Provider-specific diagnostic information reported with a completion.</summary>
/// <param name="Provider">The provider name (for example "Ollama").</param>
/// <param name="ActualModel">The model that actually served the request, when reported.</param>
/// <param name="Api">The API style used (for example "native").</param>
/// <param name="Done">Whether the provider reported the stream complete.</param>
/// <param name="DoneReason">The provider's reason the stream finished, when reported.</param>
/// <param name="TotalDurationMilliseconds">Total elapsed time in milliseconds, when reported.</param>
/// <param name="LoadDurationMilliseconds">Model load time in milliseconds, when reported.</param>
/// <param name="PromptEvaluationDurationMilliseconds">Prompt evaluation time in milliseconds, when reported.</param>
/// <param name="GenerationDurationMilliseconds">Token generation time in milliseconds, when reported.</param>
/// <param name="GenerationTokensPerSecond">Token generation throughput, when reported.</param>
/// <param name="NativeToolCallCount">Number of native tool calls received, when reported.</param>
/// <param name="ContentLength">Total characters of streamed content, when reported.</param>
/// <param name="ResponseId">Provider-assigned response identifier, when reported.</param>
/// <param name="ServiceTier">Provider service tier used for the request, when reported.</param>
/// <param name="ThinkingTokens">Tokens used for provider-side reasoning or thinking, when reported.</param>
/// <param name="SystemFingerprint">The serving fingerprint, when the provider reports one (OpenAI).</param>
public sealed record LlmProviderDiagnostics(
    string Provider,
    string? ActualModel = null,
    string? Api = null,
    bool? Done = null,
    string? DoneReason = null,
    double? TotalDurationMilliseconds = null,
    double? LoadDurationMilliseconds = null,
    double? PromptEvaluationDurationMilliseconds = null,
    double? GenerationDurationMilliseconds = null,
    double? GenerationTokensPerSecond = null,
    int? NativeToolCallCount = null,
    int? ContentLength = null,
    string? ResponseId = null,
    string? ServiceTier = null,
    int? ThinkingTokens = null,
    string? SystemFingerprint = null);
