using Microsoft.Extensions.Options;
using Penghou.Baize.Router.Configuration;

namespace Penghou.Baize.Router;

/// <summary>Shares one options monitor across the lookup and router snapshots.</summary>
internal sealed class LlmRoutingMonitor(
    IOptionsMonitor<LlmRoutingOptions> options)
{
    public IOptionsMonitor<LlmRoutingOptions> Options { get; } = options;
}
