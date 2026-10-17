using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class RetainedReplicaSnapshotColdContinuation
{
    private const int ColdCuts = 2;
    private const string Fault = "retained-snapshot-cold-continuation";

    internal static async Task RunAsync(ClusterFixture fixture,
        RetainedReplicaSnapshotScenario.SnapshotState state,
        RetainedReplicaSnapshotScenario.StoppedReplica replica,
        RetainedReplicaSnapshotScenario.FinalCommand final,
        Action<bool> onRunning, CancellationToken token)
    {
        var original = await McpCallerAssertions.SdkSuccessAsync(
            await state.Clients[replica.Index].StatusAsync(token).ConfigureAwait(false)).ConfigureAwait(false);
        await ObserveAsync(fixture, replica.Name, state, final, token).ConfigureAwait(false);
        for (var cut = 0; cut < ColdCuts; cut++)
        {
            await fixture.KillContainerAsync(replica.Name, Fault, token).ConfigureAwait(false);
            onRunning(false);
            await fixture.RestartContainerAsync(replica.Name, token).ConfigureAwait(false);
            onRunning(true);
            var reopened = await McpCallerAssertions.SdkSuccessAsync(
                await state.Clients[replica.Index].StatusAsync(token).ConfigureAwait(false)).ConfigureAwait(false);
            await Assert.That(reopened.NodeId).IsEqualTo(original.NodeId);
            await Assert.That(reopened.Incarnation).IsEqualTo(original.Incarnation);
            await Assert.That(reopened.Voters).IsEqualTo(original.Voters);
            await Assert.That(reopened.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
            await Assert.That(reopened.RoutingReady).IsTrue();
            await Assert.That(reopened.Applied).IsGreaterThanOrEqualTo(final.Receipt.Token.Position);
            await Assert.That(reopened.ReadGeneration).IsGreaterThanOrEqualTo(original.ReadGeneration);
            await ObserveAsync(fixture, replica.Name, state, final, token).ConfigureAwait(false);
        }
        await RetainedReplicaSnapshotColdAssertions.HealthyAsync(
            fixture, replica.Name, state, final, token).ConfigureAwait(false);
    }

    private static async Task ObserveAsync(ClusterFixture fixture, string node,
        RetainedReplicaSnapshotScenario.SnapshotState state,
        RetainedReplicaSnapshotScenario.FinalCommand final, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var mcp = await McpOfficialClient.ConnectAsync(
                fixture, node, fixture.AdminKey, token).ConfigureAwait(false);
            await ServerFailureObserver.ObserveAsync(() =>
                RetainedReplicaSnapshotColdAssertions.FullAsync(
                    fixture.Client(node), mcp, state, final, token), failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
