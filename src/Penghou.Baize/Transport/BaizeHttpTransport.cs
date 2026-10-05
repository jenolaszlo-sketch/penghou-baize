using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Penghou.Http.Abstractions;

namespace Penghou.Baize;

/// <summary>Executes finite neutral HTTP requests through Baize's named provider client.</summary>
public sealed class BaizeHttpTransport : IHttpTransport
{
    private readonly IHttpClientFactory _clients;

    /// <summary>Creates the default transport using the configured Baize HTTP client factory.</summary>
    public BaizeHttpTransport(IHttpClientFactory clients) => _clients = clients ?? throw new ArgumentNullException(nameof(clients));

    internal IHttpClientFactory ClientFactory => _clients;

    /// <inheritdoc />
    public async ValueTask<HttpTransportResponse> SendAsync(HttpTransportRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Limits.MaximumRedirects != 0)
            throw new NotSupportedException("Baize's default HTTP transport supports zero redirects.");

        using var message = CreateMessage(request);
        var client = _clients.CreateClient(BaizeHttp.ClientName);
        var configuredTimeout = client.Timeout;
        var effectiveTimeout = configuredTimeout == Timeout.InfiniteTimeSpan || configuredTimeout > request.Limits.Timeout
            ? request.Limits.Timeout : configuredTimeout;
        var effectiveLimits = new HttpTransportLimits(request.Limits.MaximumRequestBodyBytes, request.Limits.MaximumResponseBodyBytes,
            request.Limits.MaximumResponseHeaderBytes, request.Limits.MaximumReadBytes, effectiveTimeout, request.Limits.MaximumRedirects);
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(effectiveTimeout);
        HttpResponseMessage? response = null;
        try
        {
            response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            var headers = SnapshotHeaders(response);
            if (headers.ByteCount > effectiveLimits.MaximumResponseHeaderBytes)
                throw new InvalidDataException("HTTP response headers exceeded their configured byte limit.");
            var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            return new HttpTransportResponse(request.RequestId, (int)response.StatusCode, headers,
                new OwnedResponseBody(response, stream, effectiveLimits, deadline));
        }
        catch
        {
            response?.Dispose();
            deadline.Dispose();
            throw;
        }
    }
    private static HttpRequestMessage CreateMessage(HttpTransportRequest request)
    {
        var result = new HttpRequestMessage(new HttpMethod(request.Method), request.Uri);
        try
        {
            if (request.Body is HttpBinaryBody binary)
            {
                result.Content = new ByteArrayContent(binary.GetBytes());
                result.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(binary.ContentType);
            }
            else if (request.Body is HttpMultipartBody multipart)
            {
                var content = new MultipartFormDataContent();
                foreach (var part in multipart.Parts)
                {
                    var bytes = new ByteArrayContent(part.Body.GetBytes());
                    bytes.Headers.ContentType = MediaTypeHeaderValue.Parse(part.Body.ContentType);
                    if (part.FileName is null) content.Add(bytes, part.Name);
                    else content.Add(bytes, part.Name, part.FileName);
                }
                if (content.Headers.ContentLength is not long length || length > request.Limits.MaximumRequestBodyBytes)
                { content.Dispose(); throw new InvalidDataException("Encoded multipart request exceeded its configured byte limit."); }
                result.Content = content;
            }
            else if (request.Body is not null) throw new NotSupportedException("Unsupported request body type.");

            foreach (var (name, values) in request.Headers.Fields)
                foreach (var value in values)
                    if (!result.Headers.TryAddWithoutValidation(name, value))
                    {
                        if (result.Content is null || !result.Content.Headers.TryAddWithoutValidation(name, value))
                            throw new InvalidOperationException($"HTTP header '{name}' is not supported by the request message.");
                    }
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    private static Penghou.Http.Abstractions.HttpHeaders SnapshotHeaders(HttpResponseMessage response)
    {
        var fields = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, values) in response.Headers) fields[name] = values.ToArray();
        foreach (var (name, values) in response.Content.Headers)
        {
            if (fields.TryGetValue(name, out var existing)) fields[name] = existing.Concat(values).ToArray();
            else fields[name] = values.ToArray();
        }
        return new Penghou.Http.Abstractions.HttpHeaders(fields);
    }

    private sealed class OwnedResponseBody(HttpResponseMessage response, Stream stream, HttpTransportLimits limits, CancellationTokenSource deadline) : IHttpResponseBody
    {
        private long _read;
        private int _disposed;
        public async ValueTask<HttpResponseChunk> ReadAsync(int maximumBytes, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (maximumBytes <= 0 || maximumBytes > limits.MaximumReadBytes) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
            var bytes = new byte[maximumBytes];
            var count = await stream.ReadAsync(bytes.AsMemory(), linked.Token).ConfigureAwait(false);
            var next = checked(_read + count);
            if (next > limits.MaximumResponseBodyBytes) { await DisposeAsync().ConfigureAwait(false); throw new InvalidDataException("Decoded HTTP response exceeded its configured byte limit."); }
            _read = next;
            return new HttpResponseChunk(bytes.AsMemory(0, count), count == 0);
        }
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                stream.Dispose(); response.Dispose(); deadline.Dispose();
            }
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>Adapts legacy HttpRequestMessage provider calls onto a replaceable neutral HTTP transport.</summary>
public static class BaizeHttpTransportAdapter
{
    /// <summary>Sends one finite legacy request and returns an owned legacy response.</summary>
    public static Task<HttpResponseMessage> SendAsync(IHttpTransport transport, HttpRequestMessage request) =>
        SendAsync(transport, request, CancellationToken.None);

    /// <summary>Sends one finite legacy request with caller cancellation.</summary>
    public static Task<HttpResponseMessage> SendAsync(IHttpTransport transport, HttpRequestMessage request,
        CancellationToken cancellationToken) => SendAsync(transport, request, new HttpTransportLimits(), null, cancellationToken);
    /// <summary>Sends one finite legacy request with explicit bounds and optional descriptive context.</summary>
    public static async Task<HttpResponseMessage> SendAsync(IHttpTransport transport, HttpRequestMessage request,
        HttpTransportLimits limits, HttpRequestContext? context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(limits);
        var operationDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        operationDeadline.CancelAfter(limits.Timeout);
        var ownershipTransferred = false;
        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var id = Guid.NewGuid().ToString("N");
            HttpTransportBody? body = null;
            if (request.Content is MultipartFormDataContent formData)
            {
                var parts = new List<HttpMultipartPart>();
                long aggregate = 0;
                foreach (var part in formData)
                {
                    if (parts.Count == 64) throw new InvalidDataException("Multipart requests are limited to 64 parts.");
                    var disposition = part.Headers.ContentDisposition;
                    var name = disposition?.Name?.Trim('"') ?? throw new InvalidDataException("Multipart form part is missing its field name.");
                    var fileName = disposition?.FileNameStar ?? disposition?.FileName;
                    fileName = fileName?.Trim('"');
                    var remaining = limits.MaximumRequestBodyBytes - aggregate;
                    if (remaining < 0) throw new InvalidDataException("HTTP request body exceeds its configured byte limit.");
                    var bytes = await ReadBoundedAsync(part, remaining, operationDeadline.Token).ConfigureAwait(false);
                    aggregate += bytes.LongLength;
                    parts.Add(new HttpMultipartPart(name, new HttpBinaryBody(bytes, part.Headers.ContentType?.ToString() ?? "application/octet-stream"), fileName));
                }
                body = new HttpMultipartBody(parts);
            }
            else if (request.Content is not null)
            {
                var declared = request.Content.Headers.ContentLength;
                if (declared is > 0 && declared > limits.MaximumRequestBodyBytes) throw new InvalidDataException("HTTP request body exceeds its configured byte limit.");
                var bytes = await ReadBoundedAsync(request.Content, limits.MaximumRequestBodyBytes, operationDeadline.Token).ConfigureAwait(false);
                var mediaType = request.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
                body = new HttpBinaryBody(bytes, mediaType);
            }
            var remainingTimeout = limits.Timeout - clock.Elapsed;
            if (remainingTimeout <= TimeSpan.Zero) throw new OperationCanceledException("HTTP request deadline elapsed while snapshotting its body.", operationDeadline.Token);
            var effectiveLimits = new HttpTransportLimits(limits.MaximumRequestBodyBytes, limits.MaximumResponseBodyBytes,
                limits.MaximumResponseHeaderBytes, limits.MaximumReadBytes, remainingTimeout, limits.MaximumRedirects);
            var headers = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, values) in request.Headers) headers[key] = values.ToArray();
            if (request.Content is not null)
                foreach (var (key, values) in request.Content.Headers)
                    if (!key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) && !key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) && !key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                        headers[key] = values.ToArray();
            var neutral = new HttpTransportRequest(id, request.Method.Method, request.RequestUri ?? throw new InvalidOperationException("An absolute request URI is required."), body, new Penghou.Http.Abstractions.HttpHeaders(headers), context, effectiveLimits);
            var result = await transport.SendAsync(neutral, operationDeadline.Token).ConfigureAwait(false);
            if (!StringComparer.Ordinal.Equals(result.RequestId, id)) { await result.DisposeAsync().ConfigureAwait(false); throw new InvalidOperationException("HTTP transport returned a response for a different request."); }
            if (result.Headers.ByteCount > effectiveLimits.MaximumResponseHeaderBytes) { await result.DisposeAsync().ConfigureAwait(false); throw new InvalidDataException("HTTP response headers exceeded their configured byte limit."); }
            try
            {
                var adapted = ToHttpResponseMessage(result, effectiveLimits, operationDeadline);
                ownershipTransferred = true;
                return adapted;
            }
            catch { await result.DisposeAsync().ConfigureAwait(false); throw; }
        }
        finally { if (!ownershipTransferred) operationDeadline.Dispose(); }
    }
    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, long maximum, CancellationToken cancellationToken)
    {
        using var output = new BoundedBufferStream(maximum);
        await content.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        return output.ToArray();
    }

    private sealed class BoundedBufferStream(long maximum) : Stream
    {
        private readonly MemoryStream _inner = new();
        public byte[] ToArray() => _inner.ToArray();
        public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => _inner.Length; public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }
        private void Check(int count) { if (count < 0 || _inner.Length + count > maximum) throw new InvalidDataException("HTTP request body exceeds its configured byte limit."); }
        public override void Write(byte[] buffer, int offset, int count) { Check(count); _inner.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Check(buffer.Length); _inner.Write(buffer); }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) { Check(buffer.Length); await _inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false); }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) { Check(count); return _inner.WriteAsync(buffer, offset, count, cancellationToken); }
        public override void Flush() => _inner.Flush(); public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
    }
    private static HttpResponseMessage ToHttpResponseMessage(HttpTransportResponse response, HttpTransportLimits limits, CancellationTokenSource deadline)
    {
        var message = new HttpResponseMessage((HttpStatusCode)response.StatusCode);
        var stream = new NeutralResponseStream(response, limits.MaximumReadBytes, limits.MaximumResponseBodyBytes, deadline);
        message.Content = new StreamContent(stream);
        foreach (var (name, values) in response.Headers.Fields)
            foreach (var value in values)
                if (!message.Headers.TryAddWithoutValidation(name, value) && !message.Content.Headers.TryAddWithoutValidation(name, value))
                { message.Dispose(); throw new InvalidDataException($"Unsupported response header '{name}'."); }
        return message;
    }

    private sealed class NeutralResponseStream(HttpTransportResponse response, int chunkLimit, long maximumResponseBytes, CancellationTokenSource deadline) : Stream
    {
        private byte[] _current = [];
        private int _offset;
        private bool _sourceCompleted;
        private bool _disposed;
        private long _total;
        public override bool CanRead => !_disposed; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            try
            {
                if (buffer.IsEmpty || (_sourceCompleted && _offset == _current.Length)) return 0;
                if (_offset == _current.Length)
                {
                    var requested = Math.Min(buffer.Length, chunkLimit);
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
                    var chunk = await response.Body.ReadAsync(requested, linked.Token).ConfigureAwait(false);
                    _current = chunk.GetBytes(); _offset = 0; _sourceCompleted = chunk.IsCompleted;
                    if (_current.Length > requested || (_current.Length == 0 && !chunk.IsCompleted))
                        throw new InvalidDataException("HTTP transport returned an invalid response chunk.");
                    _total = checked(_total + _current.Length);
                    if (_total > maximumResponseBytes) throw new InvalidDataException("HTTP response exceeded its configured byte limit.");
                    if (_current.Length == 0) return 0;
                }
                var copied = Math.Min(buffer.Length, _current.Length - _offset);
                _current.AsMemory(_offset, copied).CopyTo(buffer); _offset += copied; return copied;
            }
            catch
            {
                await DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        protected override void Dispose(bool disposing) { if (disposing) DisposeAsync().AsTask().GetAwaiter().GetResult(); base.Dispose(disposing); }
        public override ValueTask DisposeAsync()
        {
            if (!_disposed) { _disposed = true; deadline.Dispose(); return response.DisposeAsync(); }
            return ValueTask.CompletedTask;
        }
        public override void Flush() => throw new NotSupportedException(); public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
