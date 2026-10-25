using System.Net;
using System.Text;
using Penghou.Baize.Router;

namespace Penghou.Baize.Qualification.Execution;

// A deterministic fake OpenAI-chat-completions terminal transport. It sits
// behind the real Baize routed adapter: the adapter, router, parser and
// telemetry are the actual production path; only the terminal backend is
// synthetic. No sockets, DNS or network are involved.
internal sealed class SyntheticTransportHandler : HttpMessageHandler
{
    public const string SlowMarker = "synthetic/model-slow";

    private static readonly Dictionary<string, (string Baseline, string Tools, HttpStatusCode Status)> Responses = new(StringComparer.Ordinal)
    {
        ["synthetic/model-alpha"] = (BaselineGood, ToolsGood, HttpStatusCode.OK),
        ["synthetic/model-wrong"] = (BaselineWrong, ToolsGood, HttpStatusCode.OK),
        ["synthetic/model-toolbad"] = (BaselineGood, ToolsBadJson, HttpStatusCode.OK),
        ["synthetic/model-no-terminal"] = (BaselineNoTerminal, ToolsGood, HttpStatusCode.OK),
        [SlowMarker] = (BaselineGood, ToolsGood, HttpStatusCode.OK),
        ["synthetic/model-unavailable"] = ("synthetic service unavailable", "", HttpStatusCode.ServiceUnavailable),
        ["synthetic/model-no-usage"] = (BaselineNoUsage, ToolsGood, HttpStatusCode.OK)
    };

    public int Sends { get; private set; }
    public string? LastRequestJson { get; private set; }
    public int CompletedRequests { get; private set; }
    public int ActiveRequests => Sends - CompletedRequests;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Sends++;
        LastRequestJson = await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var model = ModelOf(LastRequestJson);
        var (baseline, tools, status) = Responses.TryGetValue(model, out var behavior) ? behavior
            : ("synthetic unknown provider model", "", HttpStatusCode.NotFound);
        if (status != HttpStatusCode.OK)
        {
            CompletedRequests++;
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(baseline, Encoding.UTF8, "text/event-stream")
            };
        }
        var wantsTools = LastRequestJson.Contains("\"tools\"", StringComparison.Ordinal);
        var body = wantsTools ? tools : baseline;
        if (model == SlowMarker)
        {
            // Emit one delta, then hold the response body until cancellation.
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(body));
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(stream)
            };
            response.Content.Headers.TryAddWithoutValidation("Content-Type", "text/event-stream");
            try
            {
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                CompletedRequests++;
            }
            return response;
        }
        CompletedRequests++;
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/event-stream")
        };
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

    private const string ToolsBadJson = """
        data: {"id":"chatcmpl-synthetic","object":"chat.completion.chunk","created":0,"model":"synthetic/model-toolbad","choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"id":"call_1","type":"function","function":{"name":"echo_value","arguments":"{\"value\":"}}]},"finish_reason":null}]}

        data: {"id":"chatcmpl-synthetic","object":"chat.completion.chunk","created":0,"model":"synthetic/model-toolbad","choices":[{"index":0,"delta":{},"finish_reason":"tool_calls"}]}

        data: [DONE]

        """;
}

internal sealed class SyntheticHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory, IDisposable
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    public void Dispose() => handler.Dispose();
}

// Returns a deterministic synthetic placeholder for the exact secret slot of
// the frozen plan, and denies every other name. No environment variable is
// ever read; no credential value or hash enters evidence.
internal sealed class SyntheticSecretProvider(string slotReference) : ISecretProvider
{
    public const string Placeholder = "synthetic-not-a-credential";
    public int SecretReads { get; private set; }

    public Task<string?> GetSecretAsync(string name, CancellationToken cancellationToken = default)
    {
        SecretReads++;
        return Task.FromResult<string?>(name == slotReference ? Placeholder : null);
    }
}
