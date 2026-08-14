using System.Net;
using System.Text;

namespace Penghou.Baize.Generation.TestShared;

/// <summary>
/// An <see cref="HttpMessageHandler"/> that records every request and serves a
/// scripted sequence of responses, or optionally throws a configured exception
/// to simulate a transport failure.
/// </summary>
public sealed class RecordingHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();
    private Exception? _throwOnSend;

    /// <summary>Creates an empty handler that records requests.</summary>
    public RecordingHandler()
    {
    }

    /// <summary>The requests observed by the handler.</summary>
    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>The body of every observed request (empty string when the request had no content).</summary>
    public List<string> RequestBodies { get; } = [];

    /// <summary>The last observed request, or null when none.</summary>
    public HttpRequestMessage? LastRequest => Requests.LastOrDefault();

    /// <summary>The last observed request body, or null when none.</summary>
    public string? LastRequestBody => RequestBodies.LastOrDefault();

    /// <summary>Enqueues a JSON success response.</summary>
    /// <param name="body">The JSON body.</param>
    /// <returns>This handler for chaining.</returns>
    public RecordingHandler ReturnJson(string body)
    {
        _responses.Enqueue(Json(body, 200));
        return this;
    }

    /// <summary>Enqueues a JSON response with the given status code.</summary>
    /// <param name="body">The JSON body.</param>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <returns>This handler for chaining.</returns>
    public RecordingHandler ReturnJson(string body, int statusCode)
    {
        _responses.Enqueue(Json(body, statusCode));
        return this;
    }

    /// <summary>Enqueues an empty response with the given status code.</summary>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <returns>This handler for chaining.</returns>
    public RecordingHandler ReturnEmpty(int statusCode = 200)
    {
        _responses.Enqueue(new HttpResponseMessage((HttpStatusCode)statusCode));
        return this;
    }

    /// <summary>Enqueues a binary response.</summary>
    /// <param name="bytes">The response bytes.</param>
    /// <param name="contentType">The response content type.</param>
    /// <returns>This handler for chaining.</returns>
    public RecordingHandler ReturnBytes(byte[] bytes, string contentType)
    {
        _responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
            {
                Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType) }
            }
        });
        return this;
    }

    /// <summary>Makes the next send throw the given exception.</summary>
    /// <param name="exception">The exception to throw.</param>
    /// <returns>This handler for chaining.</returns>
    public RecordingHandler ThrowOnSend(Exception exception)
    {
        _throwOnSend = exception;
        return this;
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);
        RequestBodies.Add(body);

        if (_throwOnSend is not null)
            throw _throwOnSend;

        return _responses.Count > 0
            ? _responses.Dequeue()
            : new HttpResponseMessage(HttpStatusCode.OK);
    }

    private static HttpResponseMessage Json(string body, int statusCode) =>
        new((HttpStatusCode)statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
}