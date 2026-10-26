using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Penghou.Baize.Qualification.Planning;

namespace Penghou.Baize.Qualification.Execution;

// The admission decision for one physical HTTP dispatch at the last
// application-controlled boundary before the terminal handler.
internal sealed record HttpAdmissionDecision(bool Admitted, string? DenialCode, string DispatchId);

internal sealed record HttpDispatchRecord(
    string AttemptId,
    string DispatchId,
    string RunId,
    string CaseId,
    string TrialId,
    int TrialSlot,
    string ModelId,
    string Destination,
    string BoundaryKind,
    int AdmissionIndex,
    int MaximumPhysicalAttempts = 1);

// Consumed by the guarded handler: the environment owns the budget authority
// and the evidence ledger. The handler is otherwise stateless per request.
internal interface IHttpAdmissionGate
{
    Task<HttpAdmissionDecision> AdmitAsync(HttpDispatchRecord dispatch, CancellationToken cancellationToken);
    Task RecordResultAsync(HttpDispatchRecord dispatch, int? statusCode, string phase, long bytesRead,
        bool completed, CancellationToken cancellationToken);
    int FailedResultRecords { get; }
}

// The last application boundary of the qualification path. It sits immediately
// before the terminal handler (recording fake backend or pinned sockets
// handler) and beneath the real Baize neutral transport, so every provider
// request crosses it. It enforces destination/method identity, the attempt
// lease, request byte ceilings, concurrency one, budget exhaustion and
// total/idle deadlines, and it records each admitted dispatch as evidence.
internal sealed class QualificationAdmissionHandler(HttpMessageHandler terminal, IHttpAdmissionGate gate, string boundaryKind) : HttpMessageHandler
{
    private readonly SemaphoreSlim _concurrency = new(1, 1);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var lease = AttemptLeaseScope.Active ?? throw new ExecutionException("http-admission-without-attempt-lease");
        if (lease.MaximumPhysicalAttempts < 1)
            throw new ExecutionException("physical-attempt-bound-invalid");
        VerifyDestination(request, lease);
        // The reviewed bound depends on these exact request properties: an
        // inference POST with content and without Expect: 100-continue cannot
        // enter SocketsHttpHandler's connection-failure replay path. Pin the
        // protocol too; HTTP/2/3 replay paths are explicitly unsupported.
        if (request.Content is null)
            throw new ExecutionException("http-admission-requires-body");
        if (request.Headers.ExpectContinue == true)
            throw new ExecutionException("http-admission-expect-continue-rejected");
        request.Version = HttpVersion.Version11;
        request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
        if (request.Content.Headers.ContentLength is { } declared && declared > lease.MaximumRequestBodyBytes)
            throw new InvalidDataException("HTTP request body exceeds the configured qualification byte ceiling.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lease.AttemptToken, lease.DeadlineToken);
        var dispatch = new HttpDispatchRecord(lease.AttemptId, Guid.NewGuid().ToString("N"), lease.RunId,
            lease.CaseId, lease.TrialId, lease.TrialSlot, lease.ModelId,
            request.RequestUri!.AbsoluteUri, boundaryKind, 0, lease.MaximumPhysicalAttempts);
        await _concurrency.WaitAsync(linked.Token).ConfigureAwait(false);
        var handedOff = false;
        var slotReleased = false;
        try
        {
            var admission = await gate.AdmitAsync(dispatch, linked.Token).ConfigureAwait(false);
            if (!admission.Admitted)
                throw new ExecutionException("http-dispatch-denied:" + admission.DenialCode);
            HttpResponseMessage response;
            try
            {
                response = await new HttpMessageInvoker(terminal).SendAsync(request, linked.Token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                await gate.RecordResultAsync(dispatch, null, "terminal-failure", 0, false, CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            await gate.RecordResultAsync(dispatch, (int)response.StatusCode, "headers-received", 0, false,
                CancellationToken.None).ConfigureAwait(false);
            if (response.Content is null)
            {
                return response;
            }
            var wrapped = new AdmissionResponseStream(await response.Content.ReadAsStreamAsync(linked.Token).ConfigureAwait(false),
                lease, dispatch, gate, ReleaseSlot);
            response.Content = new StreamContent(wrapped);
            handedOff = true;
            return response;
        }
        finally
        {
            if (!handedOff) ReleaseSlot();
        }

        void ReleaseSlot()
        {
            if (!slotReleased)
            {
                slotReleased = true;
                _concurrency.Release();
            }
        }
    }

    private static void VerifyDestination(HttpRequestMessage request, AttemptLease lease)
    {
        var uri = request.RequestUri ?? throw new ExecutionException("http-request-without-uri");
        var matchesMethod = request.Method.Method == lease.ExpectedMethod;
        var origin = uri.GetLeftPart(UriPartial.Authority);
        var matchesOrigin = origin == lease.ExpectedOrigin;
        var basePath = lease.ExpectedBasePath.TrimEnd('/');
        var matchesPath = basePath.Length == 0 || uri.AbsolutePath == basePath
            || uri.AbsolutePath.StartsWith(basePath + "/", StringComparison.Ordinal);
        var clean = uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0;
        if (!matchesMethod || !matchesOrigin || !matchesPath || !clean)
            throw new ExecutionException("http-destination-substitution");
    }

    // Enforces decoded response byte ceilings, the absolute attempt deadline
    // and a progress-based idle deadline, and releases the concurrency slot
    // only when the body is fully consumed or disposed.
    private sealed class AdmissionResponseStream(
        Stream inner,
        AttemptLease lease,
        HttpDispatchRecord dispatch,
        IHttpAdmissionGate gate,
        Action release) : Stream
    {
        private long _bytes;
        private int _disposed;
        private bool _completed;

        public override bool CanRead => _disposed == 0;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            if (_completed) return 0;
            if (DateTimeOffset.UtcNow >= lease.AbsoluteDeadline)
                throw new OperationCanceledException("attempt-deadline");
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lease.AttemptToken, lease.DeadlineToken);
            linked.CancelAfter(lease.IdleTimeout);
            int count;
            try
            {
                count = await inner.ReadAsync(buffer, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested && !cancellationToken.IsCancellationRequested
                && !lease.DeadlineToken.IsCancellationRequested && !lease.AttemptToken.IsCancellationRequested)
            {
                throw new TimeoutException("stream-idle-deadline");
            }
            var next = checked(_bytes + count);
            if (next > lease.MaximumResponseBodyBytes)
            {
                await DisposeAsync().ConfigureAwait(false);
                throw new InvalidDataException("HTTP response exceeded the configured qualification byte ceiling.");
            }
            _bytes = next;
            _completed = count == 0;
            return count;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) DisposeAsync().AsTask().GetAwaiter().GetResult(); base.Dispose(disposing); }

        public override async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                try
                {
                    try { await inner.DisposeAsync().ConfigureAwait(false); } finally { inner.Dispose(); }
                    try
                    {
                        // Best-effort supplement: the authoritative attempt
                        // evidence is recorded by the workflow; a failure here
                        // leaves a visible incomplete dispatch record instead of
                        // corrupting an already-read qualification attempt.
                        await gate.RecordResultAsync(dispatch, null, _completed ? "complete" : "disposed-incomplete",
                            _bytes, _completed, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch
                    {
                        // Counted by the gate; the dispatch record remains visibly
                        // incomplete rather than failing the qualification read.
                    }
                }
                finally
                {
                    // A faulting body disposal must never hold the concurrency
                    // slot or leak admission ownership.
                    release();
                }
            }
        }
    }
}

// A deterministic scripted terminal handler: the "backend" of every offline
// verification. Behaviors are keyed by provider model so subjects keep
// separate identities.
internal sealed class RecordingTerminalHandler : HttpMessageHandler
{
    public const string SlowMarker = "synthetic/model-slow";
    public const string SlowChunkMarker = "synthetic/model-slow-chunk";
    public const string LargeBodyMarker = "synthetic/model-large-body";
    public const string CoTerminalMarker = "synthetic/model-coterminal";

    private static readonly Dictionary<string, (string Baseline, string Tools, HttpStatusCode Status)> Responses = new(StringComparer.Ordinal)
    {
        ["synthetic/model-alpha"] = (BaselineGood, ToolsGood, HttpStatusCode.OK),
        ["synthetic/model-wrong"] = (BaselineWrong, ToolsGood, HttpStatusCode.OK),
        ["synthetic/model-toolbad"] = (BaselineGood, ToolsBadJson, HttpStatusCode.OK),
        ["synthetic/model-no-terminal"] = (BaselineNoTerminal, ToolsGood, HttpStatusCode.OK),
        [SlowMarker] = (BaselineGood, ToolsGood, HttpStatusCode.OK),
        [SlowChunkMarker] = (BaselineGood, ToolsGood, HttpStatusCode.OK),
        [LargeBodyMarker] = (BaselineGood, ToolsGood, HttpStatusCode.OK),
        [CoTerminalMarker] = (BaselineGood, ToolsCoTerminal, HttpStatusCode.OK),
        ["synthetic/model-unavailable"] = ("synthetic service unavailable", "", HttpStatusCode.ServiceUnavailable),
        ["synthetic/model-no-usage"] = (BaselineNoUsage, ToolsGood, HttpStatusCode.OK)
    };

    public int Sends { get; private set; }
    public int ActiveRequests { get; private set; }
    public string? LastRequestJson { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Sends++;
        ActiveRequests++;
        try
        {
            LastRequestJson = await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var model = ModelOf(LastRequestJson);
            var (baseline, tools, status) = Responses.TryGetValue(model, out var behavior) ? behavior
                : ("synthetic unknown provider model", "", HttpStatusCode.NotFound);
            if (status != HttpStatusCode.OK)
                return new HttpResponseMessage(status)
                {
                    Content = new StringContent(baseline, System.Text.Encoding.UTF8, "text/event-stream")
                };
            var wantsTools = LastRequestJson.Contains("\"tools\"", StringComparison.Ordinal);
            var body = wantsTools ? tools : baseline;
            if (model == SlowMarker)
            {
                // Hold after headers until cancellation.
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(body)))
                };
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                return response;
            }
            if (model == SlowChunkMarker)
            {
                // Stream one chunk, then stall: exercises the idle deadline.
                var bytes = System.Text.Encoding.UTF8.GetBytes(body);
                var content = new StalledStream(bytes, TimeSpan.FromSeconds(30));
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(content) };
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
                return response;
            }
            if (model == LargeBodyMarker)
            {
                var oversized = new byte[256 * 1024];
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(oversized)
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "text/event-stream")
            };
        }
        finally
        {
            ActiveRequests--;
        }
    }

    private sealed class StalledStream(byte[] body, TimeSpan stallAfter) : Stream
    {
        private readonly MemoryStream _inner = new(body);
        private readonly TimeSpan _stall = stallAfter;
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var count = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (count == 0) return 0;
            await Task.Delay(_stall, cancellationToken).ConfigureAwait(false);
            return count;
        }
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
        public override ValueTask DisposeAsync() { Dispose(true); return ValueTask.CompletedTask; }
    }

    private static string ModelOf(string requestJson)
    {
        var marker = "\"model\":\"";
        var start = requestJson.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return "";
        start += marker.Length;
        var end = requestJson.IndexOf('"', start);
        return end < 0 ? "" : requestJson[start..end];
    }

    private const string BaselineGood = """
        data: {"id":"chatcmpl-synthetic","object":"chat.completion.chunk","created":0,"model":"synthetic/model-alpha","choices":[{"index":0,"delta":{"content":"\nBAIZE_"},"finish_reason":null}]}

        data: {"id":"chatcmpl-synthetic","object":"chat.completion.chunk","created":0,"model":"synthetic/model-alpha","choices":[{"index":0,"delta":{"content":"OK\r\n"},"finish_reason":"stop"}]}

        data: {"id":"chatcmpl-synthetic","object":"chat.completion.chunk","created":0,"model":"synthetic/model-alpha","choices":[],"usage":{"prompt_tokens":12,"completion_tokens":3,"total_tokens":15}}

        data: [DONE]

        """;

    private const string BaselineNoUsage = """
        data: {"id":"chatcmpl-synthetic","object":"chat.completion.chunk","created":0,"model":"synthetic/model-alpha","choices":[{"index":0,"delta":{"content":"\nBAIZE_"},"finish_reason":null}]}

        data: {"id":"chatcmpl-synthetic","object":"chat.completion.chunk","created":0,"model":"synthetic/model-alpha","choices":[{"index":0,"delta":{"content":"OK\r\n"},"finish_reason":"stop"}]}

        data: [DONE]

        """;

    private const string BaselineWrong = """
        data: {"id":"chatcmpl-synthetic","object":"chat.completion.chunk","created":0,"model":"synthetic/model-wrong","choices":[{"index":0,"delta":{"content":"WRONG_VALUE"},"finish_reason":"stop"}]}

        data: [DONE]

        """;

    private const string BaselineNoTerminal = """
        data: {"id":"chatcmpl-synthetic","object":"chat.completion.chunk","created":0,"model":"synthetic/model-no-terminal","choices":[{"index":0,"delta":{"content":"BAIZE_OK"},"finish_reason":null}]}

        data: [DONE]

        """;

    private const string ToolsGood = """
        data: {"id":"chatcmpl-synthetic","object":"chat.completion.chunk","created":0,"model":"synthetic/model-alpha","choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"id":"call_1","type":"function","function":{"name":"echo_value","arguments":"{\"value\":\"baize-live\"}"}}]},"finish_reason":null}]}

        data: {"id":"chatcmpl-synthetic","object":"chat.completion.chunk","created":0,"model":"synthetic/model-alpha","choices":[{"index":0,"delta":{},"finish_reason":"tool_calls"}]}

        data: {"id":"chatcmpl-synthetic","object":"chat.completion.chunk","created":0,"model":"synthetic/model-alpha","choices":[],"usage":{"prompt_tokens":24,"completion_tokens":6,"total_tokens":30}}

        data: [DONE]

        """;

    // Preserved BQ-1a F1 reproduction: the tool-call delta and the finish
    // reason share one wire choice. Baize's current adapter emits the finish
    // before the tool delta, so the strict streaming-integrity assertion must
    // keep failing. This fixture exists to keep the defect reproducible.
    private const string ToolsCoTerminal = """
        data: {"id":"chatcmpl-synthetic","object":"chat.completion.chunk","created":0,"model":"synthetic/model-coterminal","choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"id":"call_1","type":"function","function":{"name":"echo_value","arguments":"{\"value\":\"baize-live\"}"}}]},"finish_reason":"tool_calls"}]}

        data: [DONE]

        """;

    private const string ToolsBadJson = """
        data: {"id":"chatcmpl-synthetic","object":"chat.completion.chunk","created":0,"model":"synthetic/model-toolbad","choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"id":"call_1","type":"function","function":{"name":"echo_value","arguments":"{\"value\":"}}]},"finish_reason":null}]}

        data: {"id":"chatcmpl-synthetic","object":"chat.completion.chunk","created":0,"model":"synthetic/model-toolbad","choices":[{"index":0,"delta":{},"finish_reason":"tool_calls"}]}

        data: [DONE]

        """;
}

// The pinned real terminal handler for live execution: no automatic
// redirects, no cookies, no credentials; runtime-internal replay below this
// boundary remains unobservable and is recorded as an eligibility blocker,
// never as a proven wire count.
internal static class PinnedTerminalHandlers
{
    public static HttpMessageHandler CreateRealNetworkTerminal() => TransportProfiles.Http11Exact.CreateHandler();
}

internal sealed class GuardedHttpClientFactory(HttpMessageHandler chainRoot, bool disposeHandler) : IHttpClientFactory, IDisposable
{
    private HttpMessageHandler? _handler = chainRoot;
    public HttpClient CreateClient(string name) => new(Volatile.Read(ref _handler) ?? throw new ObjectDisposedException(nameof(GuardedHttpClientFactory)), disposeHandler: false)
    {
        Timeout = Timeout.InfiniteTimeSpan,
        DefaultRequestVersion = HttpVersion.Version11,
        DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
    };

    public void Dispose()
    {
        var handler = Interlocked.Exchange(ref _handler, null);
        if (disposeHandler) handler?.Dispose();
    }
}
