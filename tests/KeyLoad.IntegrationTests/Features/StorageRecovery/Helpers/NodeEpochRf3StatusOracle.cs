using Aspire.Hosting;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.StorageRecovery;

internal sealed record NodeEpochRf3NodeObservation(string Name, NodeStatus Status, AdminNodeSnapshot Admin);

internal static class NodeEpochRf3StatusOracle
{
    private static readonly string[] ExpectedVoters =
    [
        "http://node1:8080",
        "http://node2:8080",
        "http://node3:8080"
    ];

    internal static async Task<NodeEpochRf3NodeObservation[]> CaptureAsync(DistributedApplication app,
        NodeEpochRf3Profile profile, CancellationToken cancellationToken)
    {
        var results = new List<NodeEpochRf3NodeObservation>(NodeEpochRf3Protocol.NodeCount);
        foreach (var name in new[] { NodeEpochRf3Protocol.Node1, NodeEpochRf3Protocol.Node2, NodeEpochRf3Protocol.Node3 })
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var callers = await NodeEpochRf3Callers.ConnectAsync(app, name, name,
                profile.AdminKey, cancellationToken).ConfigureAwait(false);
            var status = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.StatusAsync(cancellationToken)
                .ConfigureAwait(false)).ConfigureAwait(false);
            var dashboard = await callers.Mcp.Client.CallToolAsync(AdminDashboardProtocol.SnapshotTool,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var snapshot = await McpCallerAssertions.SuccessAsync<AdminNodeSnapshot>(dashboard).ConfigureAwait(false);
            await Assert.That(snapshot.Value.Node.NodeId).IsEqualTo(status.NodeId);
            await Assert.That(snapshot.Value.Node.Incarnation).IsEqualTo(status.Incarnation);
            await Assert.That(snapshot.Value.Node.Voters).IsEqualTo(status.Voters);
            await Assert.That(snapshot.Value.Node.Applied).IsGreaterThanOrEqualTo(status.Applied);
            await Assert.That(status.Incarnation).IsEqualTo(profile.Incarnation);
            await Assert.That(status.Applied).IsGreaterThanOrEqualTo(0L);
            await Assert.That(status.Voters).IsEqualTo(NodeEpochRf3Protocol.NodeCount);
            await Assert.That(status.RoutingReady).IsTrue();
            results.Add(new(name, status, snapshot.Value));
        }
        await VerifyMembershipAsync(results).ConfigureAwait(false);
        return [.. results];
    }

    internal static async Task VerifyMembershipAsync(IReadOnlyList<NodeEpochRf3NodeObservation> nodes)
    {
        await Assert.That(nodes.Count).IsEqualTo(NodeEpochRf3Protocol.NodeCount);
        var voters = nodes[0].Admin.Voters;
        await Assert.That(voters.Length).IsEqualTo(NodeEpochRf3Protocol.NodeCount);
        await Assert.That(voters.SequenceEqual(ExpectedVoters, StringComparer.Ordinal)).IsTrue();
        await Assert.That(voters.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(NodeEpochRf3Protocol.NodeCount);
        foreach (var node in nodes)
        {
            await Assert.That(node.Admin.Voters.SequenceEqual(voters)).IsTrue();
            var expectedLocal = "http://" + node.Name + ":8080";
            var localVoter = node.Admin.LocalVoter
                ?? throw new InvalidOperationException("A healthy RF3 node did not report its local voter.");
            await Assert.That(localVoter).IsEqualTo(expectedLocal);
            await Assert.That(voters.Contains(localVoter, StringComparer.Ordinal)).IsTrue();
            var leader = node.Status.Leader
                ?? throw new InvalidOperationException("A healthy RF3 node did not report its leader voter.");
            await Assert.That(voters.Contains(leader, StringComparer.Ordinal)).IsTrue();
        }
        await Assert.That(nodes.Select(node => node.Admin.LocalVoter).Distinct(StringComparer.Ordinal).Count())
            .IsEqualTo(NodeEpochRf3Protocol.NodeCount);
        await Assert.That(nodes.Select(node => node.Status.NodeId).Distinct(StringComparer.Ordinal).Count())
            .IsEqualTo(NodeEpochRf3Protocol.NodeCount);
    }

    internal static async Task AssertSamePhysicalTopologyAsync(
        IReadOnlyList<NodeEpochRf3NodeObservation> prior, IReadOnlyList<NodeEpochRf3NodeObservation> current)
    {
        await Assert.That(current.Count).IsEqualTo(prior.Count);
        for (var index = 0; index < prior.Count; index++)
        {
            await Assert.That(current[index].Name).IsEqualTo(prior[index].Name);
            await Assert.That(current[index].Status.NodeId).IsEqualTo(prior[index].Status.NodeId);
            await Assert.That(current[index].Status.Incarnation).IsEqualTo(prior[index].Status.Incarnation);
            await Assert.That(current[index].Admin.LocalVoter).IsEqualTo(prior[index].Admin.LocalVoter);
            await Assert.That(current[index].Admin.Voters.SequenceEqual(prior[index].Admin.Voters,
                StringComparer.Ordinal)).IsTrue();
        }
    }

    internal static async Task EventuallyCaughtUpAsync(DistributedApplication app,
        NodeEpochRf3Profile profile, long minimumApplied, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var allReady = true;
            foreach (var node in new[] { NodeEpochRf3Protocol.Node1, NodeEpochRf3Protocol.Node2, NodeEpochRf3Protocol.Node3 })
            {
                using var http = McpCallerHttp.Create(app, node);
                var client = new KeyLoad.Client.KeyLoadClient(http, profile.AdminKey);
                var status = await client.StatusAsync(cancellationToken).ConfigureAwait(false);
                allReady &= status.IsSuccess && status.Value!.RoutingReady && status.Value.Applied >= minimumApplied;
            }
            if (allReady)
            { return; }
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
        }
    }
}
