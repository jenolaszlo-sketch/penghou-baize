using System.Net;

namespace Penghou.Baize.Diagnostics;

internal sealed class CapturingHttpContent : HttpContent
{
    private readonly HttpContent _inner;
    private readonly HttpDiagnosticSession _session;
    private int _streamCreated;

    public CapturingHttpContent(HttpContent inner, HttpDiagnosticSession session)
    {
        _inner = inner;
        _session = session;
        foreach (var header in inner.Headers)
            Headers.TryAddWithoutValidation(header.Key, header.Value);
    }

    protected override async Task SerializeToStreamAsync(
        Stream stream,
        TransportContext? context)
    {
        await using var source = await CreateContentReadStreamAsync();
        await source.CopyToAsync(stream);
    }

    protected override async Task<Stream> CreateContentReadStreamAsync()
    {
        var source = await _inner.ReadAsStreamAsync();
        return CreateCaptureStream(source);
    }

    protected override async Task<Stream> CreateContentReadStreamAsync(
        CancellationToken cancellationToken)
    {
        var source = await _inner.ReadAsStreamAsync(cancellationToken);
        return CreateCaptureStream(source);
    }

    protected override bool TryComputeLength(out long length)
    {
        if (_inner.Headers.ContentLength is { } contentLength)
        {
            length = contentLength;
            return true;
        }

        length = 0;
        return false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
            if (Volatile.Read(ref _streamCreated) == 0)
            {
                _session.TryAppendTextAsync(
                    $"{Environment.NewLine}Response body was never read.{Environment.NewLine}")
                    .AsTask().GetAwaiter().GetResult();
                _session.Complete("not-read");
            }
        }

        base.Dispose(disposing);
    }

    private Stream CreateCaptureStream(Stream source)
    {
        Interlocked.Exchange(ref _streamCreated, 1);
        return new CapturingReadStream(source, _session);
    }
}
