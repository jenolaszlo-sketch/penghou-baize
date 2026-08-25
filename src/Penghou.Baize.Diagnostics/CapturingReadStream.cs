namespace Penghou.Baize.Diagnostics;

internal sealed class CapturingReadStream : Stream
{
    private readonly Stream _inner;
    private readonly HttpDiagnosticSession _session;
    private FileStream? _capture;
    private long _capturedBytes;
    private bool _truncated;
    private int _completionRecorded;
    private int _disposed;

    public CapturingReadStream(Stream inner, HttpDiagnosticSession session)
    {
        _inner = inner;
        _session = session;
        if (session.CaptureResponseBody)
        {
            try
            {
                _capture = new FileStream(
                    session.RawResponsePath,
                    new FileStreamOptions
                    {
                        Mode = FileMode.Create,
                        Access = FileAccess.Write,
                        Share = FileShare.Read | FileShare.Write | FileShare.Delete,
                        Options = FileOptions.Asynchronous | FileOptions.SequentialScan
                    });
            }
            catch (Exception exception)
            {
                if (!session.HandleCaptureFailure(exception, "open-response-body"))
                    throw;
            }
        }
    }

    public override bool CanRead => _inner.CanRead;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        try
        {
            var read = _inner.Read(buffer, offset, count);
            if (read == 0)
                Complete("completed");
            else
                Capture(buffer.AsSpan(offset, read));
            return read;
        }
        catch (Exception exception)
        {
            Complete("stream-failed", exception);
            throw;
        }
    }

    public override async Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        var read = await ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
        return read;
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var read = await _inner.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                await CompleteAsync("completed");
            else
                await CaptureAsync(buffer[..read], cancellationToken);
            return read;
        }
        catch (Exception exception)
        {
            await CompleteAsync("stream-failed", exception);
            throw;
        }
    }

    public override void Flush() => _inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) =>
        _inner.FlushAsync(cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            if (Volatile.Read(ref _completionRecorded) == 0)
                Complete("disposed-before-end");
            DisposeCapture();
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            if (Volatile.Read(ref _completionRecorded) == 0)
                await CompleteAsync("disposed-before-end");
            await DisposeCaptureAsync();
            await _inner.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }

    private void Capture(ReadOnlySpan<byte> buffer)
    {
        if (_capture is null)
            return;

        var remaining = _session.MaxBodyBytes - _capturedBytes;
        if (remaining <= 0)
        {
            RecordTruncation();
            return;
        }

        var length = (int)Math.Min(buffer.Length, remaining);
        try
        {
            _capture.Write(buffer[..length]);
            if (_session.FlushEachResponseChunk)
                _capture.Flush();
            _capturedBytes += length;
            _session.RecordBytes(length);
            if (length < buffer.Length)
                RecordTruncation();
        }
        catch (Exception exception)
        {
            DisableCapture(exception, "write-response-body");
        }
    }

    private async ValueTask CaptureAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken)
    {
        if (_capture is null)
            return;

        var remaining = _session.MaxBodyBytes - _capturedBytes;
        if (remaining <= 0)
        {
            RecordTruncation();
            return;
        }

        var length = (int)Math.Min(buffer.Length, remaining);
        try
        {
            await _capture.WriteAsync(buffer[..length], cancellationToken);
            if (_session.FlushEachResponseChunk)
                await _capture.FlushAsync(cancellationToken);
            _capturedBytes += length;
            _session.RecordBytes(length);
            if (length < buffer.Length)
                RecordTruncation();
        }
        catch (Exception exception)
        {
            DisableCapture(exception, "write-response-body");
        }
    }

    private void DisableCapture(Exception exception, string operation)
    {
        if (!_session.HandleCaptureFailure(exception, operation))
            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(exception)
                .Throw();
        DisposeCapture();
    }

    private void RecordTruncation()
    {
        if (_truncated)
            return;
        _truncated = true;
        _session.RecordTruncation("response");
    }

    private void Complete(string outcome, Exception? exception = null)
    {
        if (Interlocked.Exchange(ref _completionRecorded, 1) != 0)
            return;
        FlushCapture();
        var details = CompletionText(outcome, exception);
        _session.TryAppendText(details);
        _session.Complete(outcome);
    }

    private async ValueTask CompleteAsync(
        string outcome,
        Exception? exception = null)
    {
        if (Interlocked.Exchange(ref _completionRecorded, 1) != 0)
            return;
        await FlushCaptureAsync();
        await _session.TryAppendTextAsync(CompletionText(outcome, exception));
        _session.Complete(outcome);
    }

    private string CompletionText(string outcome, Exception? exception) =>
        $"""

        Timestamp: {DateTimeOffset.UtcNow:O}
        Response stream outcome: {outcome}.
        Captured bytes: {_capturedBytes}.
        Truncated: {_truncated}.
        {exception}

        """;

    private void FlushCapture()
    {
        if (_capture is null)
            return;

        try
        {
            _capture.Flush();
        }
        catch (Exception exception)
        {
            DisableCapture(exception, "flush-response-body");
        }
    }

    private async ValueTask FlushCaptureAsync()
    {
        if (_capture is null)
            return;

        try
        {
            await _capture.FlushAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            DisableCapture(exception, "flush-response-body");
        }
    }

    private void DisposeCapture()
    {
        if (_capture is null)
            return;

        try
        {
            _capture.Dispose();
        }
        catch (Exception exception)
        {
            if (!_session.HandleCaptureFailure(exception, "close-response-body"))
                System.Runtime.ExceptionServices.ExceptionDispatchInfo
                    .Capture(exception)
                    .Throw();
        }
        finally
        {
            _capture = null;
        }
    }

    private async ValueTask DisposeCaptureAsync()
    {
        if (_capture is null)
            return;

        try
        {
            await _capture.DisposeAsync();
        }
        catch (Exception exception)
        {
            if (!_session.HandleCaptureFailure(exception, "close-response-body"))
                System.Runtime.ExceptionServices.ExceptionDispatchInfo
                    .Capture(exception)
                    .Throw();
        }
        finally
        {
            _capture = null;
        }
    }
}
