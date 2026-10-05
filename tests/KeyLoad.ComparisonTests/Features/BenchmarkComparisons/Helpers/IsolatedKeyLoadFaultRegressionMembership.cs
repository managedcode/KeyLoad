using Aspire.Hosting;
using KeyLoad.Client;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Retains only nonsecret public identity, ordered native voters and the observed apply cut.</summary>
internal sealed record IsolatedKeyLoadFaultRegressionMembership(int Index, string NodeId, Guid Incarnation,
    string LocalVoter, string[] Voters, string Leader, long Applied, int ProcessId, Guid ProcessInstance)
{
    internal static async Task<IsolatedKeyLoadFaultRegressionMembership[]> WaitAsync(DistributedApplication app,
        int nodeCount, string admin, long position, CancellationToken token)
    {
        using var deadline = IsolatedKeyLoadFaultRegressionProtocol.Deadline(IsolatedKeyLoadFaultRegressionProtocol.ReadinessSeconds, token);
        while (true)
        {
            deadline.Token.ThrowIfCancellationRequested();
            var nodes = await ReadAllAsync(app, nodeCount, admin, deadline.Token);
            if (Ready(nodes, nodeCount, position))
            {
                return [.. nodes.OrderBy(item => item.Index)];
            }
            await Task.Delay(IsolatedKeyLoadFaultRegressionProtocol.PollMilliseconds, deadline.Token);
        }
    }

    private static async Task<List<IsolatedKeyLoadFaultRegressionMembership>> ReadAllAsync(DistributedApplication app,
        int nodeCount, string admin, CancellationToken token)
    {
        var nodes = new List<IsolatedKeyLoadFaultRegressionMembership>(nodeCount);
        var voters = Enumerable.Range(1, nodeCount).Select(IsolatedKeyLoadFaultRegressionProtocol.Voter).ToArray();
        for (var index = 1; index <= nodeCount; index++)
        {
            using var http = IsolatedKeyLoadPublicRegressionProtocol.CreateHttp(app, index);
            using var attempt = IsolatedKeyLoadFaultRegressionProtocol.Deadline(IsolatedKeyLoadFaultRegressionProtocol.AttemptSeconds, token);
            var result = await new KeyLoadClient(http, admin, ComparisonClientOptions.Execution()).DashboardAsync(attempt.Token);
            attempt.Token.ThrowIfCancellationRequested();
            if (!result.IsSuccess)
            {
                IsolatedKeyLoadFaultRegressionProtocol.Require(result.Problem?.ErrorCode == nameof(ErrorCode.OwnershipLost));
                return nodes;
            }
            var snapshot = result.Value!;
            var local = snapshot.LocalVoter;
            var leader = snapshot.Node.Leader;
            if (!snapshot.Node.RoutingReady || local is null || leader is null)
            {
                return nodes;
            }
            var physical = Array.IndexOf(voters, local) + 1;
            IsolatedKeyLoadFaultRegressionProtocol.Require(Guid.TryParse(snapshot.Node.NodeId, out var nodeId) && nodeId != Guid.Empty
                && physical > 0 && voters.Contains(leader, StringComparer.Ordinal)
                && snapshot.Voters.SequenceEqual(voters, StringComparer.Ordinal));
            nodes.Add(new(physical, snapshot.Node.NodeId, snapshot.Node.Incarnation, local,
                [.. snapshot.Voters], leader, snapshot.Node.Applied, snapshot.Node.ProcessId, snapshot.Http.ProcessInstance));
            await Assert.That(snapshot.Node.Voters).IsEqualTo(nodeCount);
            await Assert.That(snapshot.Node.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        }
        return nodes;
    }

    private static bool Ready(List<IsolatedKeyLoadFaultRegressionMembership> nodes, int count, long position)
        => nodes.Count == count && nodes.All(item => item.Applied >= position && item.Incarnation != Guid.Empty
            && item.Voters.Length == count && item.ProcessInstance != Guid.Empty && item.ProcessId > 0)
            && nodes.Select(item => item.NodeId).Distinct(StringComparer.Ordinal).Count() == count
            && nodes.Select(item => item.LocalVoter).Distinct(StringComparer.Ordinal).Count() == count
            && nodes.Select(item => item.Incarnation).Distinct().Count() == 1
            && nodes.All(item => item.Leader == nodes[0].Leader
                && item.Voters.SequenceEqual(nodes[0].Voters, StringComparer.Ordinal))
            && nodes.Count(item => item.LocalVoter == nodes[0].Leader) == 1
            && nodes[0].Voters.ToHashSet(StringComparer.Ordinal).SetEquals(nodes.Select(item => item.LocalVoter));
}
