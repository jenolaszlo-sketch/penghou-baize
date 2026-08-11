namespace Penghou.Baize.Diagnostics;

/// <summary>Controls opt-in HTTP request and response capture.</summary>
public sealed class HttpTrafficCaptureOptions
{
    /// <summary>Whether HTTP traffic capture is active. Defaults to false.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Capture directory. Relative paths are resolved from
    /// <see cref="AppContext.BaseDirectory"/>.
    /// </summary>
    public string DirectoryPath { get; set; } = Path.Combine("logs", "baize", "http");

    /// <summary>Whether request bodies, which can contain prompts, are captured.</summary>
    public bool CaptureRequestBody { get; set; } = true;

    /// <summary>Whether raw provider response bytes are captured.</summary>
    public bool CaptureResponseBody { get; set; } = true;

    /// <summary>Maximum captured bytes per request or response body.</summary>
    public long MaxBodyBytes { get; set; } = 512 * 1024;

    /// <summary>
    /// Maximum retained request sessions. The oldest sessions are removed;
    /// zero disables automatic retention cleanup.
    /// </summary>
    public int MaxRetainedSessions { get; set; } = 100;

    /// <summary>
    /// Whether capture I/O failures are logged and ignored. Disable only when
    /// a missing diagnostic artifact should fail the model request.
    /// </summary>
    public bool ContinueOnCaptureError { get; set; } = true;

    /// <summary>
    /// Whether each captured response chunk is flushed immediately. This is
    /// useful when investigating process crashes but can reduce throughput.
    /// </summary>
    public bool FlushEachResponseChunk { get; set; }
}
