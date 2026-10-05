using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Preserves the centrally bound per-voter replay pools and their actual topology validation.</summary>
internal sealed class ReplicaReplayOptionsFactory(IOptions<NodeOptions> node) : IOptionsFactory<ReplicaReplayLimits>
{
    public ReplicaReplayLimits Create(string name)
    {
        var options = node.Value;
        options.ReplayAdmission.Validate(options.Peers.Count);
        return options.ReplayAdmission;
    }
}
