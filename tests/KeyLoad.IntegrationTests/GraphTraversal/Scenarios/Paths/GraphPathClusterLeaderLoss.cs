using System.Collections.Immutable;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class GraphPathClusterLeaderLoss
{
    private const string FaultScenario = "graph-path-cluster-leader-loss";
    private const string NewEdgeId = "0-committed-shortcut";

    internal static async Task ExecuteAsync(ClusterFixture fixture, GraphPathClusterSeed seed,
        GraphPathClusterNodeClients clients, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        string? stoppedNode = null;
        var restored = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var leaderIndex = await FindLeaderIndexAsync(clients.Administrators, cancellationToken).ConfigureAwait(false);
            await VerifyInitialPathAsync(seed, clients.Readers[leaderIndex], cancellationToken).ConfigureAwait(false);
            stoppedNode = GraphPathClusterNodeClients.Nodes[leaderIndex];
            await fixture.KillContainerAsync(stoppedNode, FaultScenario, cancellationToken).ConfigureAwait(false);
            await WaitForSurvivorsAsync(clients.Administrators, leaderIndex, cancellationToken).ConfigureAwait(false);
            var survivor = (leaderIndex + 1) % GraphPathClusterNodeClients.Nodes.Length;
            var committedToken = await CommitAndVerifyAsync(fixture, seed, clients, survivor, cancellationToken)
                .ConfigureAwait(false);
            await fixture.RestartContainerAsync(stoppedNode, cancellationToken).ConfigureAwait(false);
            await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(stoppedNode,
                WaitBehavior.WaitOnResourceUnavailable, cancellationToken).ConfigureAwait(false);
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
            [seed.Source, seed.FirstBranch, seed.Target], ["a-branch-a", "a-end-a"], GraphPathClusterScenario.Label);
    }

    private static async Task<int> FindLeaderIndexAsync(KeyLoadClient[] administrators,
        CancellationToken cancellationToken)
    {
        var statuses = (await Task.WhenAll(administrators.Select(client => client.StatusAsync(cancellationToken))))
            .Select(ClusterReplicationTestSupport.Success).ToArray();
        await Assert.That(statuses.All(status => status.Voters == GraphPathClusterNodeClients.Nodes.Length)).IsTrue();
        await Assert.That(statuses.All(status => status.RoutingReady)).IsTrue();
        await Assert.That(statuses.All(status => string.Equals(status.Leader, statuses[0].Leader,
            StringComparison.Ordinal))).IsTrue();
        var leader = new Uri(statuses[0].Leader ?? throw new InvalidOperationException("CLUSTER leader was absent."));
        return Enumerable.Range(0, GraphPathClusterNodeClients.Nodes.Length)
            .Single(index => string.Equals(leader.Host, GraphPathClusterNodeClients.Nodes[index], StringComparison.Ordinal));
    }

    private static async Task WaitForSurvivorsAsync(KeyLoadClient[] administrators, int stoppedIndex,
        CancellationToken cancellationToken)
    {
        await ClusterReplicationTestSupport.EventuallyAsync(async () =>
        {
            var survivors = Enumerable.Range(0, GraphPathClusterNodeClients.Nodes.Length)
                .Where(index => index != stoppedIndex).ToArray();
            var replies = await Task.WhenAll(survivors.Select(index => administrators[index].StatusAsync(cancellationToken)));
            if (replies.Any(reply => !reply.IsSuccess))
            {
                return false;
            }
            var statuses = replies.Select(reply => reply.Value!).ToArray();
            var leader = statuses[0].Leader;
            return statuses.All(status => status.RoutingReady && status.Voters == GraphPathClusterNodeClients.Nodes.Length
                    && string.Equals(status.Leader, leader, StringComparison.Ordinal))
                && Uri.TryCreate(leader, UriKind.Absolute, out var address)
                && survivors.Any(index => string.Equals(address.Host,
                    GraphPathClusterNodeClients.Nodes[index], StringComparison.Ordinal));
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
        var request = GraphPathClusterScenario.Request(seed, labels: ImmutableArray.Create(GraphPathClusterScenario.Label));
        var result = await McpCallerAssertions.SdkSuccessAsync(await clients.Readers[survivor]
            .ShortestPathAsync(request, cancellationToken));
        await GraphPathClusterCommandReconciliation.AssertShortcutAsync(result, command);
        await GraphPathClusterAssertions.AssertPathAsync(result, [seed.Source, seed.Target], [NewEdgeId],
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
                return reply.IsSuccess && reply.Value!.RoutingReady
                    && reply.Value.Voters == GraphPathClusterNodeClients.Nodes.Length;
            }, cancellationToken);
        }
        var statuses = (await Task.WhenAll(administrators.Select(client => client.StatusAsync(cancellationToken))))
            .Select(ClusterReplicationTestSupport.Success).ToArray();
        await Assert.That(statuses.All(status => status.RoutingReady
            && status.Voters == GraphPathClusterNodeClients.Nodes.Length
            && (!expectedIncarnation.HasValue || status.Incarnation == expectedIncarnation.Value))).IsTrue();
        await Assert.That(statuses.All(status => string.Equals(status.Leader, statuses[0].Leader,
            StringComparison.Ordinal))).IsTrue();
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
            await GraphPathClusterAssertions.AssertPathAsync(result, [seed.Source, seed.Target], [NewEdgeId],
                GraphPathClusterScenario.Label, committedToken.Position);
        }
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, restartedNode,
            seed.Reader.Secret, cancellationToken);
        var official = await McpCallerAssertions.SuccessAsync<GraphShortestPathResult>(await mcp.CallAsync(
            GraphPathClusterScenario.DirectTool, request, cancellationToken));
        await GraphPathClusterAssertions.AssertPathAsync(official.Value, [seed.Source, seed.Target], [NewEdgeId],
            GraphPathClusterScenario.Label, committedToken.Position);
        await GraphPathClusterCommandReconciliation.AssertShortcutAsync(official.Value, seed, NewEdgeId);
    }

    private static async Task RestoreNodeAsync(ClusterFixture fixture, string node,
        KeyLoadClient[] administrators, List<Exception> failures)
    {
        using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(45), TimeProvider.System);
        await ServerFailureObserver.ObserveAsync(() => fixture.RestartContainerAsync(node, recovery.Token), failures)
            .ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(node,
            WaitBehavior.WaitOnResourceUnavailable, recovery.Token), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => WaitForAllNodesAsync(administrators, null, recovery.Token), failures)
            .ConfigureAwait(false);
    }
}
