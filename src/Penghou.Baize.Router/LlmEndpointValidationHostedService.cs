using Microsoft.Extensions.Hosting;

namespace Penghou.Baize.Router;

internal sealed class LlmEndpointValidationHostedService(
    ILlmEndpointValidator validator) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var report = await validator.ValidateAsync(cancellationToken).ConfigureAwait(false);
        if (report.Succeeded) return;

        var failures = report.Endpoints.Where(endpoint => !endpoint.Succeeded).ToArray();
        var details = string.Join(
            "; ",
            failures.Select(failure => $"{failure.EndpointId}: {failure.Error}"));
        throw new LlmConfigurationException(
            LlmConfigurationFailureKind.EndpointInitialization,
            $"LLM endpoint startup validation failed. {details}",
            failures);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
