using System.Runtime.CompilerServices;

namespace Penghou.Baize.TestProvider;

public sealed class TestLlmClientProvider : ILlmClientProvider
{
    public LlmProviderKey Key { get; } = new("custom-test");

    public string DefaultBaseUrl => "https://custom-provider.example/v1";

    public LlmEndpointCapabilities DefaultCapabilities { get; } = new()
    {
        NativeToolCalling = true,
        NativeStructuredOutput = true,
        StreamingToolCallArguments = true
    };

    public ILlmClient CreateClient(LlmClientProviderContext context) =>
        new TestLlmClient(context);
}

public sealed class TestLlmClient(LlmClientProviderContext context) : ILlmClient
{
    public string Model { get; } = context.Model;

    public string BaseUrl { get; } = context.BaseUrl;

    public IReadOnlyDictionary<string, string> Settings { get; } = context.Settings;

    public LlmEndpointCapabilities Capabilities { get; } = context.Capabilities;

    public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        LlmRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        cancellationToken.ThrowIfCancellationRequested();
        yield return new LlmStreamEvent(Delta: "custom-provider");
        yield return new LlmStreamEvent(FinishReason: "stop");
    }
}
