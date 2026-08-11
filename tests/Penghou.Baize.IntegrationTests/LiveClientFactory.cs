using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Penghou.Baize.Batch;
using Penghou.Baize.Diagnostics;
using Penghou.Baize.Router.Extensions;

namespace Penghou.Baize.IntegrationTests;

internal static class LiveClientFactory
{
    public static ServiceProvider Create(
        LiveTestSettings settings,
        bool tools,
        LlmContentType? inlineMediaType = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["LlmRouting:ProviderModules:0:Assembly"] = settings.ProviderAssembly,
            ["LlmRouting:Models:0:Name"] = "live",
            ["LlmRouting:Models:0:Endpoints:0:Id"] = "live-endpoint",
            ["LlmRouting:Models:0:Endpoints:0:Provider"] = settings.Provider,
            ["LlmRouting:Models:0:Endpoints:0:ProviderModel"] = settings.Model,
            ["LlmRouting:Models:0:Endpoints:0:ApiKeySecretName"] = settings.SecretName,
            ["LlmRouting:Models:0:Endpoints:0:BaseUrl"] = settings.BaseUrl
        };
        if (tools)
        {
            values["LlmRouting:Models:0:Endpoints:0:Capabilities:NativeToolCalling"] =
                "true";
            values["LlmRouting:Models:0:Endpoints:0:Capabilities:StreamingToolCallArguments"] =
                "true";
        }
        if (inlineMediaType is not null)
        {
            values["LlmRouting:Models:0:Endpoints:0:Capabilities:ContentTypes:0"] =
                "Text";
            values["LlmRouting:Models:0:Endpoints:0:Capabilities:ContentTypes:1"] =
                inlineMediaType.Value.ToString();
            values[$"LlmRouting:Models:0:Endpoints:0:Capabilities:ContentTransports:{inlineMediaType.Value}"] =
                "InlineData";
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder
            .AddConsole()
            .SetMinimumLevel(LogLevel.Debug));
        services.AddBaizeHttpDiagnostics(options =>
        {
            options.Enabled = true;
            options.DirectoryPath = settings.DiagnosticsDirectory;
            options.MaxBodyBytes = 1024 * 1024;
            options.MaxRetainedSessions = 100;
        });
        services.AddLlmRouting(configuration);
        services.AddBaizeBatch();
        return services.BuildServiceProvider(validateScopes: true);
    }
}
