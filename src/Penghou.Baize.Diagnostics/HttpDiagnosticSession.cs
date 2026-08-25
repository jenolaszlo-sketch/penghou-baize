using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Penghou.Baize.Diagnostics;

internal sealed class HttpDiagnosticSession
{
    private readonly HttpTrafficCaptureOptions _options;
    private readonly ILogger _logger;
    private readonly long _started = Stopwatch.GetTimestamp();
    private int _completed;

    public HttpDiagnosticSession(
        string directory,
        string identifier,
        HttpTrafficCaptureOptions options,
        ILogger logger)
    {
        _options = options;
        _logger = logger;
        Identifier = identifier;
        RequestLogPath = Path.Combine(directory, $"{identifier}.request.log");
        ResponseLogPath = Path.Combine(directory, $"{identifier}.response.log");
        RawResponsePath = Path.Combine(directory, $"{identifier}.response.raw");
    }

    public string Identifier { get; }

    public string RequestLogPath { get; }

    public string ResponseLogPath { get; }

    public string RawResponsePath { get; }

    public bool ContinueOnError => _options.ContinueOnCaptureError;

    public bool FlushEachResponseChunk => _options.FlushEachResponseChunk;

    public long MaxBodyBytes => _options.MaxBodyBytes;

    public bool CaptureResponseBody => _options.CaptureResponseBody;

    public async ValueTask TryWriteAllTextAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        try
        {
            await File.WriteAllTextAsync(
                path,
                content,
                new UTF8Encoding(false),
                cancellationToken);
        }
        catch (Exception exception) when (HandleCaptureFailure(exception, "write"))
        {
        }
    }

    public async ValueTask TryAppendTextAsync(string content)
    {
        try
        {
            await File.AppendAllTextAsync(
                ResponseLogPath,
                content,
                new UTF8Encoding(false),
                CancellationToken.None);
        }
        catch (Exception exception) when (HandleCaptureFailure(exception, "append"))
        {
        }
    }

    public void TryAppendText(string content)
    {
        try
        {
            File.AppendAllText(
                ResponseLogPath,
                content,
                new UTF8Encoding(false));
        }
        catch (Exception exception) when (HandleCaptureFailure(exception, "append"))
        {
        }
    }

    public bool HandleCaptureFailure(Exception exception, string operation)
    {
        DiagnosticsTelemetry.Failures.Add(
            1,
            new KeyValuePair<string, object?>("baize.diagnostics.operation", operation),
            new KeyValuePair<string, object?>("error.type", exception.GetType().Name));
        _logger.LogWarning(
            exception,
            "Baize diagnostic capture {Operation} failed for session {DiagnosticSessionId}",
            operation,
            Identifier);
        return ContinueOnError;
    }

    public void RecordBytes(long count)
    {
        if (count > 0)
            DiagnosticsTelemetry.CapturedBytes.Add(count);
    }

    public void RecordTruncation(string direction)
    {
        DiagnosticsTelemetry.TruncatedBodies.Add(
            1,
            new KeyValuePair<string, object?>("baize.diagnostics.direction", direction));
    }

    public void Complete(string outcome)
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0)
            return;

        DiagnosticsTelemetry.Duration.Record(
            Stopwatch.GetElapsedTime(_started).TotalMilliseconds,
            new KeyValuePair<string, object?>("baize.diagnostics.outcome", outcome));
        _logger.LogDebug(
            "Baize diagnostic capture {DiagnosticSessionId} completed with {Outcome}",
            Identifier,
            outcome);
    }
}
