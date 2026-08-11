using Microsoft.Extensions.DependencyInjection;
using Penghou.Baize;
using Penghou.Baize.Ollama;
using Penghou.Baize.Router;
using Penghou.Baize.Router.Extensions;

var services = new ServiceCollection();
services.AddHttpClient();
services.AddOllamaLlmProvider();
services.AddLlmRouting(routes => routes
    .AddModel("local-coder", model => model.AddEndpoint(
        "Ollama",
        endpoint => endpoint
            .WithId("local-qwen")
            .UseProviderModel("qwen2.5-coder:7b")
            .UseBaseUrl("http://localhost:11434")))
    .AddStrategy(ModelStrategy.Auto, "local-coder")
    .AddNamedRoute("coding", "local-coder"));

await using var provider = services.BuildServiceProvider();
var router = provider.GetRequiredService<ILlmRouter>();
var explanation = await router.ExplainRouteAsync("coding");

Console.WriteLine($"Selected: {explanation.SelectedEndpoint?.EndpointId}");
Console.WriteLine("Replace ExplainRouteAsync with CompleteRouteAsync to send a request.");
