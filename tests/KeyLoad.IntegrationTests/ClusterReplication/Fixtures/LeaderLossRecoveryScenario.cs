using Aspire.Hosting.ApplicationModel;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class LeaderLossRecoveryScenario
{
    private const int NodeCount = 3;
    private const string LeadershipFailure = "leader-election-minority";
    private const string Collection = "orders";
    private const string Materialized = "materialized";

    internal static async Task VerifyMinorityAndRecoveryAsync(ClusterFixture fixture, LeaderLossRunState state,
        long projectionThroughSequence, ISet<string> stoppedContainers, CancellationToken cancellationToken)
    {
        var surviving = state.Clients[state.Survivors[0]];
        _ = ClusterReplicationTestSupport.Success(await state.Clients[state.Survivors[1]].StatusAsync(cancellationToken));
        var stoppedNode = ClusterReplicationTestSupport.NodeName(state.Survivors[1] + 1);
        stoppedContainers.Add(stoppedNode);
        await fixture.KillContainerAsync(stoppedNode,
            LeadershipFailure, cancellationToken);
        await Task.Delay(TimeSpan.FromSeconds(3), TimeProvider.System, cancellationToken);
        var denied = await surviving.CommitAsync(
            new(Guid.NewGuid(), state.Partition, [new PutDocument(Collection, "minority", "{}")]), cancellationToken);
        await Assert.That(denied.IsFailed).IsTrue();
        await Assert.That((await surviving.GetAsync(new(state.Partition, Collection, "o1"), cancellationToken)).IsFailed).IsTrue();
        foreach (var index in Enumerable.Range(0, NodeCount).Where(index => index != state.Survivors[0]))
        {
            var node = ClusterReplicationTestSupport.NodeName(index + 1);
            await fixture.RestartContainerAsync(node, cancellationToken);
            stoppedContainers.Remove(node);
        }

        await ClusterReplicationTestSupport.EventuallyAsync(
            async () => (await surviving.StatusAsync(cancellationToken)).IsSuccess, cancellationToken);
        await Assert.That(ClusterReplicationTestSupport.Success(
            await surviving.GetAsync(new(state.Partition, Collection, "minority"), cancellationToken)).Document).IsNull();
        await VerifyAllNodesReadyAsync(fixture, state, projectionThroughSequence, cancellationToken);
    }

    private static async Task VerifyAllNodesReadyAsync(ClusterFixture fixture, LeaderLossRunState state,
        long projectionThroughSequence, CancellationToken cancellationToken)
    {
        foreach (var index in Enumerable.Range(0, NodeCount))
        {
            await ClusterReplicationTestSupport.EventuallyAsync(async () =>
            {
                var current = await state.Clients[index].StatusAsync(cancellationToken);
                return current.IsSuccess && current.Value!.RoutingReady;
            }, cancellationToken);
            await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(
                ClusterReplicationTestSupport.NodeName(index + 1), WaitBehavior.WaitOnResourceUnavailable, cancellationToken);
            await Assert.That(ClusterReplicationTestSupport.Success(
                await state.Clients[index].SubscriptionStatusAsync(state.Subscription, cancellationToken)).Checkpoint).IsEqualTo(3);
            await Assert.That(ClusterReplicationTestSupport.Success(
                await state.Clients[index].GetAsync(new(state.Partition, Collection, "o1"), cancellationToken)).Document!.Revision).IsEqualTo(3);
            await Assert.That(ClusterReplicationTestSupport.Success(
                    await state.Clients[index].OutboxStatusAsync(state.Partition, cancellationToken))
                .Consumers.Single().Checkpoint).IsEqualTo(projectionThroughSequence);
            await Assert.That(ClusterReplicationTestSupport.Success(
                await state.Clients[index].GetAsync(new(state.Partition, Materialized, "o1"), cancellationToken)).Document!.Revision).IsEqualTo(1);
        }
    }
}
