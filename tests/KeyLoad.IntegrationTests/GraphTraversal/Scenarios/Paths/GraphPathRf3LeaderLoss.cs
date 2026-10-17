using System.Collections.Immutable;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class GraphPathRf3LeaderLoss
{
    private const string FaultScenario = "graph-path-rf3-leader-loss";
    private const string NewEdgeId = "0-committed-shortcut";

    internal static async Task ExecuteAsync(ClusterFixture fixture, GraphPathRf3Seed seed,
        GraphPathRf3NodeClients clients, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        string? stoppedNode = null;
        var restored = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var leaderIndex = await FindLeaderIndexAsync(clients.Administrators, cancellationToken).ConfigureAwait(false);
            await VerifyInitialPathAsync(seed, clients.Readers[leaderIndex], cancellationToken).ConfigureAwait(false);
            stoppedNode = GraphPathRf3NodeClients.Nodes[leaderIndex];
            await fixture.KillContainerAsync(stoppedNode, FaultScenario, cancellationToken).ConfigureAwait(false);
            await WaitForSurvivorsAsync(clients.Administrators, leaderIndex, cancellationToken).ConfigureAwait(false);
            var survivor = (leaderIndex + 1) % GraphPathRf3NodeClients.Nodes.Length;
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

    private static async Task VerifyInitialPathAsync(GraphPathRf3Seed seed, KeyLoadClient reader,
        CancellationToken cancellationToken)
    {
        var request = GraphPathRf3Scenario.Request(seed, labels: ImmutableArray.Create(GraphPathRf3Scenario.Label));
        var result = await McpCallerAssertions.SdkSuccessAsync(await reader.ShortestPathAsync(request, cancellationToken));
        await GraphPathRf3Assertions.AssertPathAsync(result,
            [seed.Source, seed.FirstBranch, seed.Target], ["a-branch-a", "a-end-a"], GraphPathRf3Scenario.Label);
    }

    private static async Task<int> FindLeaderIndexAsync(KeyLoadClient[] administrators,
        CancellationToken cancellationToken)
    {
        var statuses = (await Task.WhenAll(administrators.Select(client => client.StatusAsync(cancellationToken))))
            .Select(ClusterReplicationTestSupport.Success).ToArray();
        await Assert.That(statuses.All(status => status.Voters == GraphPathRf3NodeClients.Nodes.Length)).IsTrue();
        await Assert.That(statuses.All(status => status.RoutingReady)).IsTrue();
        await Assert.That(statuses.All(status => string.Equals(status.Leader, statuses[0].Leader,
            StringComparison.Ordinal))).IsTrue();
        var leader = new Uri(statuses[0].Leader ?? throw new InvalidOperationException("RF3 leader was absent."));
        return Enumerable.Range(0, GraphPathRf3NodeClients.Nodes.Length)
            .Single(index => string.Equals(leader.Host, GraphPathRf3NodeClients.Nodes[index], StringComparison.Ordinal));
    }

    private static async Task WaitForSurvivorsAsync(KeyLoadClient[] administrators, int stoppedIndex,
        CancellationToken cancellationToken)
    {
        await ClusterReplicationTestSupport.EventuallyAsync(async () =>
        {
            var survivors = Enumerable.Range(0, GraphPathRf3NodeClients.Nodes.Length)
                .Where(index => index != stoppedIndex).ToArray();
            var replies = await Task.WhenAll(survivors.Select(index => administrators[index].StatusAsync(cancellationToken)));
            if (replies.Any(reply => !reply.IsSuccess))
            {
                return false;
            }
            var statuses = replies.Select(reply => reply.Value!).ToArray();
            var leader = statuses[0].Leader;
            return statuses.All(status => status.RoutingReady && status.Voters == GraphPathRf3NodeClients.Nodes.Length
                    && string.Equals(status.Leader, leader, StringComparison.Ordinal))
                && Uri.TryCreate(leader, UriKind.Absolute, out var address)
                && survivors.Any(index => string.Equals(address.Host,
                    GraphPathRf3NodeClients.Nodes[index], StringComparison.Ordinal));
        }, cancellationToken);
    }

    private static async Task<CommitToken> CommitAndVerifyAsync(ClusterFixture fixture, GraphPathRf3Seed seed,
        GraphPathRf3NodeClients clients, int survivor, CancellationToken cancellationToken)
    {
        var command = new CommandRequest(Guid.NewGuid(), seed.Partition,
        [new UpsertEdge(GraphPathRf3Scenario.Graph, NewEdgeId, seed.Source, seed.Target, GraphPathRf3Scenario.Label)]);
        var receipt = await GraphPathRf3CommandReconciliation.CommitAsync(clients.Administrators[survivor],
            command, cancellationToken);
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Mutations).HasSingleItem();
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(seed.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Incarnation).IsNotEqualTo(Guid.Empty);
        var request = GraphPathRf3Scenario.Request(seed, labels: ImmutableArray.Create(GraphPathRf3Scenario.Label));
        var result = await McpCallerAssertions.SdkSuccessAsync(await clients.Readers[survivor]
            .ShortestPathAsync(request, cancellationToken));
        await GraphPathRf3CommandReconciliation.AssertShortcutAsync(result, command);
        await GraphPathRf3Assertions.AssertPathAsync(result, [seed.Source, seed.Target], [NewEdgeId],
            GraphPathRf3Scenario.Label, receipt.Token.Position);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, GraphPathRf3NodeClients.Nodes[survivor],
            seed.Reader.Secret, cancellationToken);
        var official = await McpCallerAssertions.SuccessAsync<GraphShortestPathResult>(await mcp.CallAsync(
            GraphPathRf3Scenario.DirectTool, request, cancellationToken));
        await GraphPathRf3Assertions.AssertEquivalentAsync(result, official.Value);
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
                    && reply.Value.Voters == GraphPathRf3NodeClients.Nodes.Length;
            }, cancellationToken);
        }
        var statuses = (await Task.WhenAll(administrators.Select(client => client.StatusAsync(cancellationToken))))
            .Select(ClusterReplicationTestSupport.Success).ToArray();
        await Assert.That(statuses.All(status => status.RoutingReady
            && status.Voters == GraphPathRf3NodeClients.Nodes.Length
            && (!expectedIncarnation.HasValue || status.Incarnation == expectedIncarnation.Value))).IsTrue();
        await Assert.That(statuses.All(status => string.Equals(status.Leader, statuses[0].Leader,
            StringComparison.Ordinal))).IsTrue();
    }

    private static async Task VerifyEveryNodeAsync(ClusterFixture fixture, GraphPathRf3Seed seed,
        GraphPathRf3NodeClients clients, string restartedNode, CommitToken committedToken,
        CancellationToken cancellationToken)
    {
        var request = GraphPathRf3Scenario.Request(seed, labels: ImmutableArray.Create(GraphPathRf3Scenario.Label));
        foreach (var nodeIndex in Enumerable.Range(0, GraphPathRf3NodeClients.Nodes.Length))
        {
            var result = await McpCallerAssertions.SdkSuccessAsync(await clients.Readers[nodeIndex]
                .ShortestPathAsync(request, cancellationToken));
            await GraphPathRf3CommandReconciliation.AssertShortcutAsync(result, seed, NewEdgeId);
            await GraphPathRf3Assertions.AssertPathAsync(result, [seed.Source, seed.Target], [NewEdgeId],
                GraphPathRf3Scenario.Label, committedToken.Position);
        }
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, restartedNode,
            seed.Reader.Secret, cancellationToken);
        var official = await McpCallerAssertions.SuccessAsync<GraphShortestPathResult>(await mcp.CallAsync(
            GraphPathRf3Scenario.DirectTool, request, cancellationToken));
        await GraphPathRf3Assertions.AssertPathAsync(official.Value, [seed.Source, seed.Target], [NewEdgeId],
            GraphPathRf3Scenario.Label, committedToken.Position);
        await GraphPathRf3CommandReconciliation.AssertShortcutAsync(official.Value, seed, NewEdgeId);
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
