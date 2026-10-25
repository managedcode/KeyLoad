using System.Collections.Immutable;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.StorageReplication;
using KeyLoad.Server;

namespace KeyLoad.ClusterTests.GraphTraversal;

internal static class GraphPathClusterSiloRestart
{
    private const string FaultScenario = "graph-path-cluster-silo-restart";
    private const string NewEdgeId = "0-committed-shortcut";

    internal static async Task ExecuteAsync(ClusterFixture fixture, GraphPathClusterSeed seed,
        GraphPathClusterNodeClients clients, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        string? stoppedNode = null;
        var restored = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var selectedIndex = await SelectSiloIndexAsync(clients.Administrators, cancellationToken).ConfigureAwait(false);
            await VerifyInitialPathAsync(seed, clients.Readers[selectedIndex], cancellationToken).ConfigureAwait(false);
            stoppedNode = GraphPathClusterNodeClients.Nodes[selectedIndex];
            await fixture.KillServerAsync(stoppedNode, FaultScenario, cancellationToken).ConfigureAwait(false);
            await WaitForSurvivorsAsync(clients.Administrators, selectedIndex, cancellationToken).ConfigureAwait(false);
            var survivor = (selectedIndex + 1) % GraphPathClusterNodeClients.Nodes.Length;
            await VerifyInitialPathAsync(seed, clients.Readers[survivor], cancellationToken).ConfigureAwait(false);
            await fixture.RestartServerAsync(stoppedNode, cancellationToken).ConfigureAwait(false);
            await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(stoppedNode,
                WaitBehavior.WaitOnResourceUnavailable, cancellationToken).ConfigureAwait(false);
            var committedToken = await CommitAndVerifyAsync(fixture, seed, clients, survivor, cancellationToken)
                .ConfigureAwait(false);
            await WaitForAllNodesAsync(clients.Administrators, committedToken.Incarnation, cancellationToken)
                .ConfigureAwait(false);
            restored = true;
            await VerifyEveryNodeAsync(fixture, seed, clients, stoppedNode, committedToken, cancellationToken)
                .ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (failures.Count > 0)
        {
            await ServerFailureObserver.ObserveAsync(fixture.SaveFailureDiagnosticsAsync, failures).ConfigureAwait(false);
        }
        if (stoppedNode is not null && !restored)
        {
            await RestoreNodeAsync(fixture, stoppedNode, clients.Administrators, failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task VerifyInitialPathAsync(GraphPathClusterSeed seed, KeyLoadClient reader,
        CancellationToken cancellationToken)
    {
        var request = GraphPathClusterScenario.Request(seed, labels: ImmutableArray.Create(GraphPathClusterScenario.Label));
        var result = await McpCallerAssertions.SdkSuccessAsync(await reader.ShortestPathAsync(request, cancellationToken));
        await GraphPathClusterAssertions.AssertPathAsync(result,
            seed.StoredVertices([seed.Source, seed.FirstBranch, seed.Target]), ["a-branch-a", "a-end-a"], GraphPathClusterScenario.Label);
    }

    private static async Task<int> SelectSiloIndexAsync(KeyLoadClient[] administrators,
        CancellationToken cancellationToken)
    {
        var statuses = (await Task.WhenAll(administrators.Select(client => client.StatusAsync(cancellationToken))))
            .Select(ClusterReplicationTestSupport.Success).ToArray();
        await Assert.That(statuses.All(status => status.RoutingReady && status.SiloCount == GraphPathClusterNodeClients.Nodes.Length
            && status.ReplicaCount == GraphPathClusterNodeClients.Nodes.Length)).IsTrue();
        await Assert.That(statuses.Select(status => status.NodeId).Distinct(StringComparer.Ordinal).Count()).IsEqualTo(GraphPathClusterNodeClients.Nodes.Length);
        return GraphPathClusterNodeClients.Nodes.Length - 1;
    }

    private static async Task WaitForSurvivorsAsync(KeyLoadClient[] administrators, int stoppedIndex,
        CancellationToken cancellationToken)
    {
        await ClusterReplicationTestSupport.EventuallyAsync(async () =>
        {
            var survivors = Enumerable.Range(0, GraphPathClusterNodeClients.Nodes.Length)
                .Where(index => index != stoppedIndex).ToArray();
            var replies = await Task.WhenAll(survivors.Select(index => administrators[index].StatusAsync(cancellationToken)));
            return replies.All(reply => reply.IsSuccess && reply.Value is { RoutingReady: true } status
                && status.SiloCount == GraphPathClusterNodeClients.Nodes.Length
                && status.ReplicaCount == GraphPathClusterNodeClients.Nodes.Length);
        }, cancellationToken);
    }

    private static async Task<CommitToken> CommitAndVerifyAsync(ClusterFixture fixture, GraphPathClusterSeed seed,
        GraphPathClusterNodeClients clients, int survivor, CancellationToken cancellationToken)
    {
        var command = new CommandRequest(Guid.NewGuid(), seed.Partition,
        [new UpsertEdge(GraphPathClusterScenario.Graph, NewEdgeId, seed.Source, seed.Target, GraphPathClusterScenario.Label)]);
        var receipt = await GraphPathClusterCommandReconciliation.CommitAsync(clients.Administrators[survivor],
            command, cancellationToken);
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Mutations).HasSingleItem();
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(seed.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Incarnation).IsNotEqualTo(Guid.Empty);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(receipt.PersistedCopies).IsEqualTo(GraphPathClusterNodeClients.Nodes.Length);
        var request = GraphPathClusterScenario.Request(seed, labels: ImmutableArray.Create(GraphPathClusterScenario.Label));
        var result = await McpCallerAssertions.SdkSuccessAsync(await clients.Readers[survivor]
            .ShortestPathAsync(request, cancellationToken));
        await GraphPathClusterCommandReconciliation.AssertShortcutAsync(result, seed, command);
        await GraphPathClusterAssertions.AssertPathAsync(result, seed.StoredVertices([seed.Source, seed.Target]), [NewEdgeId],
            GraphPathClusterScenario.Label, receipt.Token.Position);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, GraphPathClusterNodeClients.Nodes[survivor],
            seed.Reader.Secret, cancellationToken);
        var official = await McpCallerAssertions.SuccessAsync<GraphShortestPathResult>(await mcp.CallAsync(
            GraphPathClusterScenario.DirectTool, request, cancellationToken));
        await GraphPathClusterAssertions.AssertEquivalentAsync(result, official.Value);
        await Assert.That(official.Value.CutPosition).IsGreaterThanOrEqualTo(receipt.Token.Position);
        return receipt.Token;
    }

    private static async Task WaitForAllNodesAsync(KeyLoadClient[] administrators,
        Guid? expectedIncarnation, CancellationToken cancellationToken)
    {
        foreach (var administrator in administrators)
        {
            await ClusterReplicationTestSupport.EventuallyAsync(async () =>
            {
                var reply = await administrator.StatusAsync(cancellationToken);
                return reply.IsSuccess && reply.Value.RoutingReady
                    && reply.Value.SiloCount == GraphPathClusterNodeClients.Nodes.Length;
            }, cancellationToken);
        }
        var statuses = (await Task.WhenAll(administrators.Select(client => client.StatusAsync(cancellationToken))))
            .Select(ClusterReplicationTestSupport.Success).ToArray();
        await Assert.That(statuses.All(status => status.RoutingReady
            && status.SiloCount == GraphPathClusterNodeClients.Nodes.Length
            && (!expectedIncarnation.HasValue || status.Incarnation == expectedIncarnation.Value))).IsTrue();
        await Assert.That(statuses.Select(status => status.NodeId).Distinct(StringComparer.Ordinal).Count())
            .IsEqualTo(GraphPathClusterNodeClients.Nodes.Length);
    }

    private static async Task VerifyEveryNodeAsync(ClusterFixture fixture, GraphPathClusterSeed seed,
        GraphPathClusterNodeClients clients, string restartedNode, CommitToken committedToken,
        CancellationToken cancellationToken)
    {
        var request = GraphPathClusterScenario.Request(seed, labels: ImmutableArray.Create(GraphPathClusterScenario.Label));
        foreach (var nodeIndex in Enumerable.Range(0, GraphPathClusterNodeClients.Nodes.Length))
        {
            var result = await McpCallerAssertions.SdkSuccessAsync(await clients.Readers[nodeIndex]
                .ShortestPathAsync(request, cancellationToken));
            await GraphPathClusterCommandReconciliation.AssertShortcutAsync(result, seed, NewEdgeId);
            await GraphPathClusterAssertions.AssertPathAsync(result, seed.StoredVertices([seed.Source, seed.Target]), [NewEdgeId],
                GraphPathClusterScenario.Label, committedToken.Position);
        }
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, restartedNode,
            seed.Reader.Secret, cancellationToken);
        var official = await McpCallerAssertions.SuccessAsync<GraphShortestPathResult>(await mcp.CallAsync(
            GraphPathClusterScenario.DirectTool, request, cancellationToken));
        await GraphPathClusterAssertions.AssertPathAsync(official.Value, seed.StoredVertices([seed.Source, seed.Target]),
            [NewEdgeId], GraphPathClusterScenario.Label, committedToken.Position);
        await GraphPathClusterCommandReconciliation.AssertShortcutAsync(official.Value, seed, NewEdgeId);
    }

    private static async Task RestoreNodeAsync(ClusterFixture fixture, string node,
        KeyLoadClient[] administrators, List<Exception> failures)
    {
        using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(45), TimeProvider.System);
        await ServerFailureObserver.ObserveAsync(() => fixture.RestartServerAsync(node, recovery.Token), failures)
            .ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(node,
            WaitBehavior.WaitOnResourceUnavailable, recovery.Token), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => WaitForAllNodesAsync(administrators, null, recovery.Token), failures)
            .ConfigureAwait(false);
    }
}
