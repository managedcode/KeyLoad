using KeyLoad.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests;

/// <summary>Explicit standalone native options composition for physical server regression fixtures.</summary>
internal static class ServerRuntimeTestOptions
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
