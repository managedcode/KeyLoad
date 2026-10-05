using KeyLoad.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.CrashHost;

/// <summary>Explicit validated central native options composition for real stopped-node upgrade fixtures.</summary>
internal static class CrashServerRuntimeOptions
{
    internal static ServerRuntimeOptions Runtime(NodeOptions node)
    {
        node.Validate();
        var services = new ServiceCollection();
        services.AddRuntimeOptions(new ConfigurationBuilder().Build());
        services.AddSingleton(Options.Create(node));
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<ServerRuntimeOptions>();
    }
}
