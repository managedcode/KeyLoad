using Aspire.Hosting.ApplicationModel;
using static KeyLoad.ClusterTests.StorageReplication.ClusterReplicationTestSupport;

namespace KeyLoad.ClusterTests.StorageReplication;

internal static class SiloRestartRecoveryScenario
{
    private const int NodeCount = 3;
    private const string SiloFailure = "silo-copy-unavailable";
    private const string Collection = "orders";
    private const string Materialized = "materialized";

    internal static async Task VerifyUnavailableCopiesAndRecoveryAsync(ClusterFixture fixture, SiloRestartRunState state,
        long projectionThroughSequence, ISet<string> stoppedProcesses, CancellationToken cancellationToken)
    {
        var surviving = state.Clients[state.Survivors[0]];
        foreach (var index in Enumerable.Range(0, NodeCount).Where(index => index != state.Survivors[0]))
        {
            var node = ClusterReplicationTestSupport.NodeName(index + 1);
            stoppedProcesses.Add(node);
            await fixture.KillServerAsync(node, SiloFailure, cancellationToken);
        }
        var pending = new CommandRequest(Guid.NewGuid(), state.Partition,
            [new PutDocument(Collection, "after-silo-loss", "{}")]);
        await ClusterReplicationTestSupport.MissingCopiesAsync(await surviving.CommitAsync(pending, cancellationToken));
        // Two of three voters are lost, so the in-memory membership keeps no quorum: the lone survivor cannot remove the
        // lost silos or admit fresh incarnations (RapidMembership RM-010), and only its refusal to acknowledge writes is
        // guaranteed. The copies return through a complete restart over their persisted stores.
        var survivor = ClusterReplicationTestSupport.NodeName(state.Survivors[0] + 1);
        stoppedProcesses.Add(survivor);
        await fixture.KillServerAsync(survivor, SiloFailure, cancellationToken);
        var nodes = Enumerable.Range(1, NodeCount).Select(ClusterReplicationTestSupport.NodeName).ToArray();
        await Task.WhenAll(nodes.Select(node => fixture.RestartServerAsync(node, cancellationToken)));
        stoppedProcesses.Clear();

        await ClusterReplicationTestSupport.EventuallyAsync(
            async () => (await surviving.StatusAsync(cancellationToken)).IsSuccess, cancellationToken);
        var restored = ClusterReplicationTestSupport.Success(await ClusterReplicationTestSupport.RetryDuringSiloRecoveryAsync(
            () => surviving.CommitAsync(pending, cancellationToken), cancellationToken));
        await Assert.That(restored.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(restored.PersistedCopies).IsEqualTo(NodeCount);
        await SubmissionColdAssertions.ReceiptAsync(ClusterReplicationTestSupport.Success(
            await surviving.CommitAsync(pending, cancellationToken)), restored);
        await Assert.That(ClusterReplicationTestSupport.Success(await surviving.GetAsync(
            new(state.Partition, Collection, "o1"), state.Receipt.Token, cancellationToken)).Document!.Revision).IsEqualTo(3);
        var document = ClusterReplicationTestSupport.Success(await surviving.GetAsync(
            new(state.Partition, Collection, "after-silo-loss"), restored.Token, cancellationToken)).Document;
        await Assert.That(document).IsNotNull();
        await Assert.That(document!.Revision).IsEqualTo(1);
        await Assert.That(document.Json).IsEqualTo("{}");
        await VerifyAllNodesReadyAsync(fixture, state, projectionThroughSequence, cancellationToken);
    }

    internal static async Task SaveFailureDiagnosticsAsync(ClusterFixture fixture, Exception failure)
    {
        try
        {
            await fixture.SaveFailureDiagnosticsAsync();
        }
        catch (Exception diagnosticsFailure) when (IsNonFatalCleanupFailure(diagnosticsFailure))
        {
            failure.Data[ClusterDiagnosticsFailureKey] = diagnosticsFailure;
        }
    }

    private static async Task VerifyAllNodesReadyAsync(ClusterFixture fixture, SiloRestartRunState state,
        long projectionThroughSequence, CancellationToken cancellationToken)
    {
        foreach (var index in Enumerable.Range(0, NodeCount))
        {
            await ClusterReplicationTestSupport.EventuallyAsync(async () =>
            {
                var current = await state.Clients[index].StatusAsync(cancellationToken);
                return current.IsSuccess && current.Value.RoutingReady;
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
