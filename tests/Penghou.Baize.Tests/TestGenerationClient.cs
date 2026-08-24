using System.Text;
using System.Text.Json;
using Penghou.Baize.Generation;

namespace Penghou.Baize.Tests;

/// <summary>
/// A minimal concrete <see cref="GenerationClientBase"/> exposing the
/// protected transport surface for testing.
/// </summary>
public sealed class TestGenerationClient(
    string provider,
    string endpointId,
    string model,
    IHttpClientFactory httpClientFactory,
    string apiKey,
    GenerationCapabilities capabilities)
    : GenerationClientBase(provider, endpointId, model, httpClientFactory, apiKey, capabilities)
{
    public override Task<GenerationOperation> SubmitAsync(
        GenerationRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new GenerationOperation(CreateHandle("op"), GenerationOperationState.Queued));

    public override Task<GenerationOperation> GetAsync(
        GenerationOperationHandle handle,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new GenerationOperation(handle, GenerationOperationState.Queued));

    public override Task<GenerationOperation> CancelAsync(
        GenerationOperationHandle handle,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new GenerationOperation(handle, GenerationOperationState.Canceled));

    public async Task<GenerationOperation> SubmitTextToImageAsync(
        bool submission,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://unit.test/operations");
        ApplyAuth(request);
        var response = await SendAsync(request, "image submission", submission, cancellationToken);
        await ReadBodyAsync(response);
        return new GenerationOperation(CreateHandle("op"), GenerationOperationState.Queued);
    }

    public async Task<JsonElement> ReadBodyAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://unit.test/status");
        using var response = await SendAsync(request, "status", false, CancellationToken.None);
        return await ReadJsonAsync(response, "status", CancellationToken.None);
    }

    private async Task<JsonElement> ReadBodyAsync(HttpResponseMessage response) =>
        await ReadJsonAsync(response, "status", CancellationToken.None);

    public async Task<T> DeserializeBodyAsync<T>()
    {
        var element = await ReadBodyAsync();
        return Deserialize<T>(element, "status");
    }

    public async Task<byte[]> ReadBytesAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://unit.test/asset");
        using var response = await SendAsync(request, "asset", false, CancellationToken.None);
        return await ReadBytesAsync(response, CancellationToken.None);
    }

    public GenerationOperationHandle MakeHandle(string id) => CreateHandle(id);

    public void ExposeValidate(GenerationRequest request) => ValidateRequest(request);

    public static ByteArrayContent BuildJsonContent(object value) => JsonContent(value);

    public static string ReadContent(ByteArrayContent content) =>
        Encoding.UTF8.GetString(content.ReadAsByteArrayAsync().GetAwaiter().GetResult());
}
