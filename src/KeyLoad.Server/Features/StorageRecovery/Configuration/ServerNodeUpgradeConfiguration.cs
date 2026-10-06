using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Retains validated replica policy while binding one temporary offline store directory.</summary>
[ConfigurationBinding]
internal static class ServerNodeUpgradeConfiguration
{
    internal static IOptions<ReplicaConfiguration> Replica(ServerRuntimeOptions options, string directory)
    {
        var projected = new OptionsManager<ReplicaConfiguration>(
            new OfflineReplicaConfigurationFactory(options.ReplicaConfiguration, directory));
        _ = projected.Value;
        return projected;
    }
}

/// <summary>Creates one validated offline directory projection from the supplied native runtime snapshot.</summary>
internal sealed class OfflineReplicaConfigurationFactory(IOptions<ReplicaConfiguration> source, string directory)
    : IOptionsFactory<ReplicaConfiguration>
{
    public ReplicaConfiguration Create(string name)
    {
        var configuration = source.Value with { Directory = directory };
        configuration.Validate();
        return configuration;
    }
}
