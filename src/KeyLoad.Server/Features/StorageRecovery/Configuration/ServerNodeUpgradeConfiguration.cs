using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Retains validated replica policy while binding one temporary offline store directory.</summary>
[ConfigurationBinding]
internal static class ServerNodeUpgradeConfiguration
{
    internal static IOptions<ReplicaConfiguration> Replica(ServerRuntimeOptions options, string directory)
    {
        var configuration = options.ReplicaConfiguration.Value with { Directory = directory };
        configuration.Validate();
        return Options.Create(configuration);
    }
}
