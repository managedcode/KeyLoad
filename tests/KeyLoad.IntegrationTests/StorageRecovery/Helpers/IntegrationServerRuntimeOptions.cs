using KeyLoad.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests;

/// <summary>Composes validated native server policy for the explicit stopped-node RF3 fixture.</summary>
internal static class IntegrationServerRuntimeOptions
{
    internal static ServerRuntimeOptions Runtime(NodeOptions node)
    {
        node.Validate();
        using var configuration = new ConfigurationManager();
        var services = new ServiceCollection();
        services.AddRuntimeOptions(configuration);
        services.AddSingleton<IOptions<NodeOptions>>(Options.Create(node));
        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<ServerRuntimeOptions>();
        runtime.ValidateBeforePhysicalOwnership();
        return runtime;
    }

}
