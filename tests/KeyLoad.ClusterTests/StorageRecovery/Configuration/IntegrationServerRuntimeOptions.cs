using KeyLoad.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.ClusterTests;

/// <summary>Composes validated execution server policy for a stopped Orleans node.</summary>
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
