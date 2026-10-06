using KeyLoad.Orleans;
using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Derives authenticated peer settings from the same validated central node and discovery options.</summary>
internal sealed class ReplicaPeerOptionsFactory(IOptions<NodeOptions> node, IOptions<ReplicaConfiguration> replica,
    IOptions<PeerDiscoveryOptions> discovery, IOptions<ReplicaReplayLimits> replay) : IOptionsFactory<ReplicaPeerOptions>
{
    public ReplicaPeerOptions Create(string name)
    {
        var options = node.Value.CreatePeerOptions(discovery.Value.ConnectTimeout) with
        { ReplayLimits = replay.Value };
        options.Validate(replica.Value);
        return options;
    }
}
