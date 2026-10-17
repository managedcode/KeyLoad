using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Derives the fixed physical replica snapshot from the centrally validated node options.</summary>
internal sealed class ReplicaConfigurationFactory(IOptions<NodeOptions> node) : IOptionsFactory<ReplicaConfiguration>
{
    public ReplicaConfiguration Create(string name)
    {
        var options = node.Value;
        var replica = options.CreateReplicaConfiguration(Path.GetFullPath(options.DataDirectory));
        replica.Validate();
        return replica;
    }
}
