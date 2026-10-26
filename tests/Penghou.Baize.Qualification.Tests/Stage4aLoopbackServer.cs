using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Penghou.Baize.Qualification.Tests;

// A controlled HTTP/1.1 server with deterministic fault modes. It records
// accepted connections and parsed requests so probes can distinguish TCP
// connections, request headers, complete requests and physical request
// attempts. No external network endpoint is contacted.
internal enum LoopbackMode
{
    Baseline,
    CloseImmediately,
    CloseAfterHeaders,
    CloseAfterBody,
    HoldAfterBody,
    PartialResponseThenClose,
    StallAfterFirstChunk,
    StreamingBaseline,
    Redirect,
    Unauthorized,
    ServiceUnavailableThenBaseline,
    CloseAfterBodyOnceThenBaseline,
    KeepAliveThenCloseOnceThenBaseline
}

internal sealed record ServerRequest(
    int ConnectionId,
    int Ordinal,
    string Method,
    string Target,
    string Version,
    bool HeadersComplete,
    bool BodyComplete,
    int ContentLength,
    int BodyBytes);

internal sealed class ScriptedLoopbackServer : IAsyncDisposable
{
    private const string BaselineSse = """
        data: {"id":"chatcmpl-synthetic","object":"chat.completion.chunk","created":0,"model":"synthetic/model-alpha","choices":[{"index":0,"delta":{"content":"\nBAIZE_"},"finish_reason":null}]}

        data: {"id":"chatcmpl-synthetic","object":"chat.completion.chunk","created":0,"model":"synthetic/model-alpha","choices":[{"index":0,"delta":{"content":"OK\r\n"},"finish_reason":"stop"}]}

        data: {"id":"chatcmpl-synthetic","object":"chat.completion.chunk","created":0,"model":"synthetic/model-alpha","choices":[],"usage":{"prompt_tokens":12,"completion_tokens":3,"total_tokens":15}}

        data: [DONE]

        """;

    private const string FirstChunk = """
        data: {"id":"chatcmpl-synthetic","object":"chat.completion.chunk","created":0,"model":"synthetic/model-alpha","choices":[{"index":0,"delta":{"content":"\nBAIZE_"},"finish_reason":null}]}

        """;

    private static readonly byte[] HeaderTerminator = "\r\n\r\n"u8.ToArray();
    private static readonly byte[] BaselineBody = Encoding.UTF8.GetBytes(BaselineSse);
    private static readonly byte[] FirstChunkBody = Encoding.UTF8.GetBytes(FirstChunk);

    private readonly TcpListener _listener;
    private readonly LoopbackMode _mode;
    private readonly string? _redirectTarget;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _acceptLoop;
    private readonly List<ServerRequest> _requests = [];
    private readonly object _sync = new();
    private readonly TaskCompletionSource _firstConnection = NewSignal();
    private readonly TaskCompletionSource _firstChunkSent = NewSignal();
    private readonly TaskCompletionSource _releaseBody = NewSignal();
    private int _connections;
    private int _ordinal;

    public ScriptedLoopbackServer(LoopbackMode mode, string? redirectTarget = null)
    {
        _mode = mode;
        _redirectTarget = redirectTarget;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start(64);
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoop = Task.Run(AcceptAsync);
    }

    public int Port { get; }
    public Task FirstConnection => _firstConnection.Task;
    public Task FirstChunkSent => _firstChunkSent.Task;

    public int ConnectionCount
    {
        get { lock (_sync) return _connections; }
    }

    public ServerRequest[] Requests
    {
        get { lock (_sync) return _requests.ToArray(); }
    }

    public int CompleteRequests
    {
        get { lock (_sync) return _requests.Count(request => request.BodyComplete); }
    }

    public int HeaderRequests
    {
        get { lock (_sync) return _requests.Count; }
    }

    public void ReleaseBody() => _releaseBody.TrySetResult();

    public void StopListening()
    {
        try { _listener.Stop(); }
        catch (SocketException) { }
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_shutdown.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (InvalidOperationException)
                {
                    // StopListening may race the accept loop in fault probes.
                    return;
                }
                catch (SocketException)
                {
                    return;
                }
                client.NoDelay = true;
                int connectionId;
                lock (_sync) connectionId = ++_connections;
                _firstConnection.TrySetResult();
                _ = Task.Run(() => HandleConnectionAsync(connectionId, client));
            }
        }
        catch (Exception) when (_shutdown.IsCancellationRequested)
        {
        }
    }

    private async Task HandleConnectionAsync(int connectionId, TcpClient client)
    {
        using (client)
        {
            var stream = client.GetStream();
            var reader = new BufferedNetworkReader(stream);
            try
            {
                if (_mode == LoopbackMode.CloseImmediately) return;
                while (!_shutdown.IsCancellationRequested)
                {
                    var headerBytes = await reader.ReadUntilAsync(HeaderTerminator, 64 * 1024, _shutdown.Token);
                    if (headerBytes is null) return;
                    var parsed = ParseHeaders(headerBytes);
                    if (_mode == LoopbackMode.CloseAfterHeaders)
                    {
                        Record(connectionId, parsed, headersComplete: true, bodyComplete: false, bodyBytes: 0);
                        return;
                    }
                    var (body, complete) = parsed.ContentLength == 0
                        ? (Array.Empty<byte>(), true)
                        : await reader.ReadExactAsync(parsed.ContentLength, _shutdown.Token);
                    var request = Record(connectionId, parsed, headersComplete: true, bodyComplete: complete, bodyBytes: body.Length);
                    if (!complete) return;

                    switch (_mode)
                    {
                        case LoopbackMode.CloseAfterBody:
                        case LoopbackMode.CloseAfterBodyOnceThenBaseline when request.Ordinal == 1:
                            return;
                        case LoopbackMode.KeepAliveThenCloseOnceThenBaseline when request.Ordinal == 1:
                            await WriteHeadersAsync(stream, "200 OK", BaselineBody.Length, keepAlive: true);
                            await stream.WriteAsync(BaselineBody, _shutdown.Token);
                            await stream.FlushAsync(_shutdown.Token);
                            return;
                        case LoopbackMode.HoldAfterBody:
                            await HoldAsync();
                            return;
                        case LoopbackMode.PartialResponseThenClose:
                            await WriteHeadersAsync(stream, "200 OK", BaselineBody.Length + 128, keepAlive: false);
                            await stream.WriteAsync(BaselineBody.AsMemory(0, BaselineBody.Length / 2), _shutdown.Token);
                            await stream.FlushAsync(_shutdown.Token);
                            return;
                        case LoopbackMode.StallAfterFirstChunk:
                            await WriteHeadersAsync(stream, "200 OK", FirstChunkBody.Length + 1024, keepAlive: false);
                            await stream.WriteAsync(FirstChunkBody, _shutdown.Token);
                            await stream.FlushAsync(_shutdown.Token);
                            _firstChunkSent.TrySetResult();
                            await HoldAsync();
                            return;
                        case LoopbackMode.StreamingBaseline:
                            await WriteHeadersAsync(stream, "200 OK", BaselineBody.Length, keepAlive: true);
                            await stream.WriteAsync(BaselineBody.AsMemory(0, BaselineBody.Length / 2), _shutdown.Token);
                            await stream.FlushAsync(_shutdown.Token);
                            _firstChunkSent.TrySetResult();
                            try { await _releaseBody.Task.WaitAsync(_shutdown.Token); }
                            catch (OperationCanceledException) { return; }
                            await stream.WriteAsync(BaselineBody.AsMemory(BaselineBody.Length / 2), _shutdown.Token);
                            await stream.FlushAsync(_shutdown.Token);
                            continue;
                        case LoopbackMode.Redirect:
                            await WriteHeadersAsync(stream, "302 Found", 0, keepAlive: true,
                                ("Location", _redirectTarget ?? "http://127.0.0.1:1/unreachable"));
                            continue;
                        case LoopbackMode.Unauthorized:
                            await WriteHeadersAsync(stream, "401 Unauthorized", 0, keepAlive: true,
                                ("WWW-Authenticate", "Basic realm=\"qualification-probe\""));
                            continue;
                        case LoopbackMode.ServiceUnavailableThenBaseline when request.Ordinal == 1:
                            await WriteHeadersAsync(stream, "503 Service Unavailable", 0, keepAlive: true);
                            continue;
                        default:
                            await WriteHeadersAsync(stream, "200 OK", BaselineBody.Length, keepAlive: true);
                            await stream.WriteAsync(BaselineBody, _shutdown.Token);
                            await stream.FlushAsync(_shutdown.Token);
                            continue;
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or SocketException
                or OperationCanceledException or ObjectDisposedException)
            {
            }
        }
    }

    private async Task HoldAsync()
    {
        try { await Task.Delay(Timeout.InfiniteTimeSpan, _shutdown.Token); }
        catch (OperationCanceledException) { }
    }

    private ServerRequest Record(int connectionId, ParsedRequest parsed, bool headersComplete, bool bodyComplete, int bodyBytes)
    {
        lock (_sync)
        {
            var request = new ServerRequest(connectionId, ++_ordinal, parsed.Method, parsed.Target, parsed.Version,
                headersComplete, bodyComplete, parsed.ContentLength, bodyBytes);
            _requests.Add(request);
            return request;
        }
    }

    private static ParsedRequest ParseHeaders(byte[] headerBytes)
    {
        var text = Encoding.Latin1.GetString(headerBytes);
        var lines = text.Split("\r\n", StringSplitOptions.None);
        var requestLine = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (requestLine.Length < 3) throw new InvalidDataException("invalid request line");
        var contentLength = 0;
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            if (line[..colon].Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(line[(colon + 1)..].Trim(), System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var value))
                contentLength = value;
        }
        return new ParsedRequest(requestLine[0], requestLine[1], requestLine[2], contentLength);
    }

    private static async Task WriteHeadersAsync(NetworkStream stream, string status, int contentLength,
        bool keepAlive, params (string Name, string Value)[] headers)
    {
        var builder = new StringBuilder();
        builder.Append("HTTP/1.1 ").Append(status).Append("\r\n");
        builder.Append("Content-Type: text/event-stream\r\n");
        foreach (var (name, value) in headers)
            builder.Append(name).Append(": ").Append(value).Append("\r\n");
        builder.Append("Content-Length: ").Append(contentLength).Append("\r\n");
        builder.Append("Connection: ").Append(keepAlive ? "keep-alive" : "close").Append("\r\n\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(builder.ToString()));
        await stream.FlushAsync();
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        StopListening();
        try { await _acceptLoop.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (TimeoutException) { }
        catch (OperationCanceledException) { }
        _shutdown.Dispose();
    }

    private sealed record ParsedRequest(string Method, string Target, string Version, int ContentLength);

    private sealed class BufferedNetworkReader(NetworkStream stream)
    {
        private readonly byte[] _buffer = new byte[8192];
        private int _offset;
        private int _count;

        public async ValueTask<byte[]?> ReadUntilAsync(byte[] delimiter, int maximum, CancellationToken cancellationToken)
        {
            var bytes = new List<byte>();
            var matched = 0;
            while (bytes.Count < maximum)
            {
                var next = await ReadByteAsync(cancellationToken);
                if (next < 0) return null;
                bytes.Add((byte)next);
                if (next == delimiter[matched])
                {
                    matched++;
                    if (matched == delimiter.Length) return bytes.ToArray();
                }
                else
                {
                    matched = next == delimiter[0] ? 1 : 0;
                }
            }
            throw new InvalidDataException("request headers exceeded the probe limit");
        }

        public async ValueTask<(byte[] Body, bool Complete)> ReadExactAsync(int count, CancellationToken cancellationToken)
        {
            var body = new byte[count];
            var read = 0;
            while (read < count && _offset < _count)
                body[read++] = _buffer[_offset++];
            while (read < count)
            {
                var now = await stream.ReadAsync(body.AsMemory(read), cancellationToken);
                if (now == 0) return (body[..read], false);
                read += now;
            }
            return (body, true);
        }

        private async ValueTask<int> ReadByteAsync(CancellationToken cancellationToken)
        {
            if (_offset == _count)
            {
                _count = await stream.ReadAsync(_buffer, cancellationToken);
                _offset = 0;
                if (_count == 0) return -1;
            }
            return _buffer[_offset++];
        }
    }
}
