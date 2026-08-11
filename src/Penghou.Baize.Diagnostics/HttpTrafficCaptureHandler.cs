using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Penghou.Baize.Diagnostics;

/// <summary>
/// Opt-in HTTP handler that records bounded request metadata and tees provider
/// response streams to disk as callers consume them.
/// </summary>
public sealed class HttpTrafficCaptureHandler(
    IOptionsMonitor<HttpTrafficCaptureOptions> options,
    ILogger<HttpTrafficCaptureHandler> logger) : DelegatingHandler
{
    private static readonly object RetentionLock = new();

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var current = options.CurrentValue;
        if (!current.Enabled)
            return await base.SendAsync(request, cancellationToken);

        var directory = ResolveDirectory(current.DirectoryPath);
        try
        {
            Directory.CreateDirectory(directory);
            ApplyRetention(directory, current.MaxRetainedSessions);
        }
        catch (Exception exception)
        {
            RecordSetupFailure(exception, current);
            if (!current.ContinueOnCaptureError)
                throw;
            return await base.SendAsync(request, cancellationToken);
        }

        var identifier = CreateRequestIdentifier();
        var session = new HttpDiagnosticSession(directory, identifier, current, logger);
        var tags = CreateTags(request);
        DiagnosticsTelemetry.Sessions.Add(1, tags);
        using var activity = BaizeTelemetry.Activities.StartActivity(
            "llm.http.capture",
            ActivityKind.Internal);
        activity?.SetTag("gen_ai.operation.name", "http_capture");
        activity?.SetTag("http.request.method", request.Method.Method);
        activity?.SetTag("url.full", HttpDiagnosticRedactor.RedactUri(request.RequestUri));
        activity?.SetTag("baize.diagnostics.session.id", identifier);

        logger.LogDebug(
            "Baize HTTP diagnostic capture {DiagnosticSessionId} started for {Method} {Uri}",
            identifier,
            request.Method,
            HttpDiagnosticRedactor.RedactUri(request.RequestUri));

        await WriteRequestLogAsync(session, request, current, cancellationToken);

        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.SetTag("error.type", exception.GetType().FullName);
            await session.TryWriteAllTextAsync(
                session.ResponseLogPath,
                CreateExceptionLog(identifier, exception),
                CancellationToken.None);
            session.Complete("request-failed");
            throw;
        }

        activity?.SetTag("http.response.status_code", (int)response.StatusCode);
        activity?.SetStatus(ActivityStatusCode.Ok);
        await WriteResponseMetadataAsync(session, response, cancellationToken);

        if (response.Content is null)
        {
            await session.TryAppendTextAsync(
                $"{Environment.NewLine}Response contained no body.{Environment.NewLine}");
            session.Complete("no-body");
        }
        else
        {
            response.Content = new CapturingHttpContent(response.Content, session);
        }

        return response;
    }

    private static async Task WriteRequestLogAsync(
        HttpDiagnosticSession session,
        HttpRequestMessage request,
        HttpTrafficCaptureOptions options,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Timestamp: {DateTimeOffset.UtcNow:O}");
        builder.AppendLine($"Diagnostic session: {session.Identifier}");
        AppendTraceContext(builder);
        builder.AppendLine($"Request: {request.Method} " +
            HttpDiagnosticRedactor.RedactUri(request.RequestUri));
        builder.AppendLine($"HTTP version: {request.Version}");
        builder.AppendLine();
        builder.AppendLine("--- REQUEST HEADERS ---");
        HttpDiagnosticRedactor.AppendHeaders(builder, request.Headers);
        if (request.Content is not null)
            HttpDiagnosticRedactor.AppendHeaders(builder, request.Content.Headers);
        builder.AppendLine();
        builder.AppendLine("--- REQUEST BODY ---");

        if (!options.CaptureRequestBody)
        {
            builder.AppendLine("[Request body capture disabled]");
        }
        else if (request.Content is null)
        {
            builder.AppendLine("[No request body]");
        }
        else if (!IsTextContent(request.Content.Headers.ContentType))
        {
            builder.AppendLine(
                $"[Body not captured because content type is " +
                $"{request.Content.Headers.ContentType}]");
        }
        else if (request.Content.Headers.ContentLength is { } length &&
                 length > options.MaxBodyBytes)
        {
            builder.AppendLine(
                $"[Request body omitted: {length} bytes exceeds the " +
                $"{options.MaxBodyBytes}-byte capture limit]");
            session.RecordTruncation("request");
        }
        else
        {
            try
            {
                var body = await request.Content.ReadAsByteArrayAsync(cancellationToken);
                var capturedLength = Math.Min(body.LongLength, options.MaxBodyBytes);
                builder.AppendLine(Encoding.UTF8.GetString(body, 0, (int)capturedLength));
                session.RecordBytes(capturedLength);
                if (capturedLength < body.LongLength)
                {
                    builder.AppendLine(
                        $"[TRUNCATED after {capturedLength} of {body.LongLength} bytes]");
                    session.RecordTruncation("request");
                }
            }
            catch (Exception exception) when
                (session.HandleCaptureFailure(exception, "read-request-body"))
            {
                builder.AppendLine("[Failed to capture request body]");
            }
        }

        builder.AppendLine();
        builder.AppendLine("--- END REQUEST ---");
        await session.TryWriteAllTextAsync(
            session.RequestLogPath,
            builder.ToString(),
            cancellationToken);
    }

    private static async Task WriteResponseMetadataAsync(
        HttpDiagnosticSession session,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Timestamp: {DateTimeOffset.UtcNow:O}");
        builder.AppendLine($"Diagnostic session: {session.Identifier}");
        AppendTraceContext(builder);
        builder.AppendLine(
            $"Response: {(int)response.StatusCode} {response.ReasonPhrase}");
        builder.AppendLine($"HTTP version: {response.Version}");
        builder.AppendLine();
        builder.AppendLine("--- RESPONSE HEADERS ---");
        HttpDiagnosticRedactor.AppendHeaders(builder, response.Headers);
        if (response.Content is not null)
            HttpDiagnosticRedactor.AppendHeaders(builder, response.Content.Headers);
        builder.AppendLine();
        builder.AppendLine("--- RESPONSE BODY ---");
        builder.AppendLine(session.CaptureResponseBody
            ? $"Raw response: {Path.GetFileName(session.RawResponsePath)}"
            : "[Response body capture disabled]");
        builder.AppendLine(
            "The response is captured incrementally while the client reads it.");
        await session.TryWriteAllTextAsync(
            session.ResponseLogPath,
            builder.ToString(),
            cancellationToken);
    }

    private static TagList CreateTags(HttpRequestMessage request) =>
        new()
        {
            { "gen_ai.operation.name", "http_capture" },
            { "http.request.method", request.Method.Method }
        };

    private static void AppendTraceContext(StringBuilder builder)
    {
        if (Activity.Current is not { } activity)
            return;

        builder.AppendLine($"Trace id: {activity.TraceId}");
        builder.AppendLine($"Span id: {activity.SpanId}");
    }

    private static string CreateExceptionLog(string identifier, Exception exception) =>
        $"""
        Timestamp: {DateTimeOffset.UtcNow:O}
        Diagnostic session: {identifier}
        HTTP request failed before a response was returned.

        --- EXCEPTION ---
        {exception}
        """;

    private static string ResolveDirectory(string directoryPath) =>
        Path.IsPathRooted(directoryPath)
            ? Path.GetFullPath(directoryPath)
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, directoryPath));

    private static string CreateRequestIdentifier()
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
        var correlation = Activity.Current?.TraceId.ToString();
        if (string.IsNullOrWhiteSpace(correlation))
            correlation = Guid.NewGuid().ToString("N");
        return $"{timestamp}-{correlation[..Math.Min(16, correlation.Length)]}-" +
               Guid.NewGuid().ToString("N")[..8];
    }

    private static bool IsTextContent(MediaTypeHeaderValue? contentType)
    {
        var mediaType = contentType?.MediaType;
        return string.IsNullOrWhiteSpace(mediaType) ||
               mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
               mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase) ||
               mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase) ||
               mediaType.Equals(
                   "application/x-www-form-urlencoded",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyRetention(string directory, int maximumSessions)
    {
        if (maximumSessions <= 0)
            return;

        lock (RetentionLock)
        {
            var expired = new DirectoryInfo(directory)
                .EnumerateFiles("*.request.log")
                .OrderByDescending(file => file.CreationTimeUtc)
                .Skip(Math.Max(0, maximumSessions - 1))
                .ToArray();
            foreach (var requestFile in expired)
            {
                var suffix = ".request.log";
                var identifier = requestFile.Name[..^suffix.Length];
                DeleteIfPresent(requestFile.FullName);
                DeleteIfPresent(Path.Combine(directory, $"{identifier}.response.log"));
                DeleteIfPresent(Path.Combine(directory, $"{identifier}.response.raw"));
            }
        }
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private void RecordSetupFailure(
        Exception exception,
        HttpTrafficCaptureOptions current)
    {
        DiagnosticsTelemetry.Failures.Add(
            1,
            new KeyValuePair<string, object?>("baize.diagnostics.operation", "setup"),
            new KeyValuePair<string, object?>("error.type", exception.GetType().Name));
        logger.LogWarning(
            exception,
            "Baize diagnostic capture could not initialize directory {DirectoryPath}",
            current.DirectoryPath);
    }
}
