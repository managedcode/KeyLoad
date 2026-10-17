using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.StorageRecovery;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class DueFaultRf3LeaderIdentity
{
    internal const string LeaderMissing = "The healthy RF3 statuses did not identify one physical leader.";

    internal static bool HasReplacementLeader(ManagedCode.Communication.Result<NodeStatus> first,
        ManagedCode.Communication.Result<NodeStatus> second, DueFaultRf3LeaderCut cut, out string leader)
    {
        leader = string.Empty;
        if (!first.IsSuccess || !second.IsSuccess || first.Value is not { RoutingReady: true, Voters: 3 } a
            || second.Value is not { RoutingReady: true, Voters: 3 } b || a.Leader is null
            || a.Leader != b.Leader || a.Leader == VoterOrigin(cut.LeaderNode))
        {
            return false;
        }
        if (!cut.Survivors.Any(name => VoterOrigin(name) == a.Leader))
        {
            return false;
        }
        leader = a.Leader;
        return true;
    }

    internal static NodeEpochRf3NodeObservation FindLeader(IReadOnlyList<NodeEpochRf3NodeObservation> nodes)
    {
        var leaders = nodes.Select(value => value.Status.Leader).Where(value => value is not null)
            .Distinct(StringComparer.Ordinal).ToArray();
        if (leaders.Length != 1)
        {
            throw new InvalidOperationException(LeaderMissing);
        }
        if (nodes.Any(node => node.Status.Leader != leaders[0]))
        {
            throw new InvalidOperationException(LeaderMissing);
        }
        return nodes.Single(value => value.Admin.LocalVoter == leaders[0]);
    }

    internal static string VoterOrigin(string node) => node switch
    {
        RequestCqrsRf3Protocol.Node1 => "http://node1:8080",
        RequestCqrsRf3Protocol.Node2 => "http://node2:8080",
        RequestCqrsRf3Protocol.Node3 => "http://node3:8080",
        _ => throw new ArgumentOutOfRangeException(nameof(node))
    };
}
