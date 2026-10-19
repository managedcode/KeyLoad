using Aspire.Hosting;
using KeyLoad.Client;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Captures execution Orleans membership and each silo's actual physical and process identity.</summary>
internal sealed record IsolatedKeyLoadFaultChecksMembership(int Index, string NodeId, Guid Incarnation,
    string SiloName, string[] ActiveSilos, long Position, int ProcessId, Guid ProcessInstance)
{
    internal static async Task<IsolatedKeyLoadFaultChecksMembership[]> WaitAsync(DistributedApplication app,
        int nodeCount, string admin, CancellationToken token)
    {
        using var deadline = IsolatedKeyLoadFaultChecksProtocol.Deadline(ExecutionPolicyFixture.Harness().Value.KeyLoadFaultReadinessTimeout, token);
        while (true)
        {
            deadline.Token.ThrowIfCancellationRequested();
            var nodes = await ReadAllAsync(app, nodeCount, admin, deadline.Token);
            if (Ready(nodes, nodeCount))
            {
                return [.. nodes.OrderBy(item => item.Index)];
            }
            await Task.Delay(ExecutionPolicyFixture.Harness().Value.KeyLoadFaultPollInterval, TimeProvider.System, deadline.Token);
        }
    }

    private static async Task<List<IsolatedKeyLoadFaultChecksMembership>> ReadAllAsync(DistributedApplication app,
        int nodeCount, string admin, CancellationToken token)
    {
        var nodes = new List<IsolatedKeyLoadFaultChecksMembership>(nodeCount);
        var expectedSilos = Enumerable.Range(1, nodeCount).Select(IsolatedKeyLoadFaultChecksProtocol.Resource).ToArray();
        for (var index = 1; index <= nodeCount; index++)
        {
            using var http = IsolatedKeyLoadPublicChecksProtocol.CreateHttp(app, index);
            using var attempt = IsolatedKeyLoadFaultChecksProtocol.Deadline(ExecutionPolicyFixture.Harness().Value.KeyLoadFaultAttemptTimeout, token);
            var result = await new KeyLoadClient(http, admin, ComparisonClientOptions.Execution()).DashboardAsync(attempt.Token);
            attempt.Token.ThrowIfCancellationRequested();
            if (!result.IsSuccess)
            {
                IsolatedKeyLoadFaultChecksProtocol.Require(result.Problem?.ErrorCode == nameof(ErrorCode.OwnershipLost));
                return nodes;
            }
            var snapshot = result.Value!;
            var local = snapshot.SiloName;
            if (!snapshot.Node.RoutingReady || local is null)
            {
                return nodes;
            }
            var physical = Array.IndexOf(expectedSilos, local) + 1;
            IsolatedKeyLoadFaultChecksProtocol.Require(Guid.TryParse(snapshot.Node.NodeId, out var nodeId) && nodeId != Guid.Empty
                && physical > 0 && snapshot.ActiveSilos.SequenceEqual(expectedSilos, StringComparer.Ordinal));
            nodes.Add(new(physical, snapshot.Node.NodeId, snapshot.Node.Incarnation, local,
                [.. snapshot.ActiveSilos], snapshot.Node.Position, snapshot.Node.ProcessId, snapshot.Http.ProcessInstance));
            await Assert.That(snapshot.Node.SiloCount).IsEqualTo(nodeCount);
            await Assert.That(snapshot.Node.ReplicaCount).IsEqualTo(nodeCount);
            await Assert.That(snapshot.Node.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        }
        return nodes;
    }

    private static bool Ready(List<IsolatedKeyLoadFaultChecksMembership> nodes, int count)
        => nodes.Count == count && nodes.All(item => item.Incarnation != Guid.Empty
            && item.ActiveSilos.Length == count && item.ProcessInstance != Guid.Empty && item.ProcessId > 0)
            && nodes.Select(item => item.NodeId).Distinct(StringComparer.Ordinal).Count() == count
            && nodes.Select(item => item.SiloName).Distinct(StringComparer.Ordinal).Count() == count
            && nodes.Select(item => item.Incarnation).Distinct().Count() == 1
            && nodes.All(item => item.ActiveSilos.SequenceEqual(nodes[0].ActiveSilos, StringComparer.Ordinal))
            && nodes[0].ActiveSilos.ToHashSet(StringComparer.Ordinal).SetEquals(nodes.Select(item => item.SiloName));
}
