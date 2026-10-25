using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.Query;
using TUnit.Assertions.Enums;
using static KeyLoad.ClusterTests.StorageReplication.ClusterReplicationTestSupport;

namespace KeyLoad.ClusterTests.StorageReplication;

internal static class SiloRestartScenario
{
    private const int NodeCount = 3;
    private const string PartitionKeyFormat = "N";
    private const string SiloFailure = "silo-copy-unavailable";
    private const string Collection = "orders";
    private const string Events = "events";
    private const string Jobs = "jobs";
    private const string Activity = "activity";
    private const string Materialized = "materialized";

    internal static async Task RunAsync(ClusterFixture fixture)
    {
        using var timeout = McpCallerDeadline.Create();
        var stoppedProcesses = new HashSet<string>(StringComparer.Ordinal);
        SiloRestartRunState? state = null;
        try
        {
            state = await PrepareAsync(fixture, timeout.Token);
            state = await RestartSelectedSiloAsync(fixture, state, stoppedProcesses, timeout.Token);
            await SiloRestartQueueScenario.ProcessAsync(state, Collection, Jobs, timeout.Token);
            var projectionBatch = await ProcessSubscriptionAsync(state, timeout.Token);
            var projectionThroughSequence = await CommitProjectionAsync(state, projectionBatch, timeout.Token);
            await SiloRestartRecoveryScenario.VerifyUnavailableCopiesAndRecoveryAsync(
                fixture, state, projectionThroughSequence, stoppedProcesses, timeout.Token);
        }
        catch (Exception failure) when (IsNonFatalCleanupFailure(failure))
        {
            await SiloRestartRecoveryScenario.SaveFailureDiagnosticsAsync(fixture, failure);
            await RestoreFailedClusterAsync(fixture, state?.Clients, stoppedProcesses, failure);
            throw;
        }
    }

    private static async Task<SiloRestartRunState> PrepareAsync(ClusterFixture fixture, CancellationToken cancellationToken)
    {
        var clients = Enumerable.Range(1, NodeCount).Select(number => fixture.Client(NodeName(number))).ToArray();
        var partition = new PartitionRef("integration", "database", Collection, Guid.NewGuid().ToString(PartitionKeyFormat));
        var resources = await ConfigureResourcesAsync(clients, partition, cancellationToken);
        var command = await SiloRestartInitialBatch.CommitAsync(clients, partition, Success, cancellationToken);
        await VerifyInitialReplicasAsync(clients, partition, resources.Subscription, cancellationToken);
        var liveRequest = new AstQueryRequest(partition, new(Collection, null, [new("*", "*")], null, [], 100), AllowFullScan: true);
        var liveSnapshot = Success(await clients[1].StartLiveQueryAsync(new(liveRequest), cancellationToken));
        return new(clients, partition, resources.ProjectionConsumer, resources.Subscription, command.Command,
            command.Receipt, liveRequest, liveSnapshot, []);
    }

    private static async Task<(ProjectionConsumerRef ProjectionConsumer, SubscriptionRef Subscription)> ConfigureResourcesAsync(
        KeyLoadClient[] clients, PartitionRef partition, CancellationToken cancellationToken)
    {
        Success(await clients[0].ConfigureResourceAsync(Guid.NewGuid(), new(partition.TenantId, partition.DatabaseId,
            new(Collection, ResourceKind.Collection, Collection)
            {
                Indexes = [new("number", ["/number"], true)]
            }), cancellationToken));
        Success(await clients[1].ConfigureResourceAsync(Guid.NewGuid(), new(partition.TenantId, partition.DatabaseId,
            new(Events, ResourceKind.StreamSet, Collection)), cancellationToken));
        Success(await clients[2].ConfigureResourceAsync(Guid.NewGuid(), new(partition.TenantId, partition.DatabaseId,
            new(Jobs, ResourceKind.WorkQueue, Collection)), cancellationToken));
        Success(await clients[0].ConfigureResourceAsync(Guid.NewGuid(), new(partition.TenantId, partition.DatabaseId,
            new(Activity, ResourceKind.Topic, Collection)), cancellationToken));
        Success(await clients[0].ConfigureResourceAsync(Guid.NewGuid(), new(partition.TenantId, partition.DatabaseId,
            new(Materialized, ResourceKind.Collection, Collection)), cancellationToken));
        var projectionConsumer = new ProjectionConsumerRef(partition, "outbox");
        Success(await clients[0].ConfigureProjectionAsync(new(Guid.NewGuid(), projectionConsumer, new(1, [Collection], [])), cancellationToken));
        var subscription = new SubscriptionRef(new(partition, Activity, EventSourceKind.Topic), "projection");
        Success(await clients[1].ConfigureSubscriptionAsync(new(Guid.NewGuid(), subscription, new("root")), cancellationToken));
        return (projectionConsumer, subscription);
    }

    private static async Task VerifyInitialReplicasAsync(KeyLoadClient[] clients, PartitionRef partition,
        SubscriptionRef subscription, CancellationToken cancellationToken)
    {
        foreach (var client in clients)
        {
            await Assert.That(Success(await client.GetAsync(new(partition, Collection, "o1"), cancellationToken)).Document!.Revision).IsEqualTo(1);
            await Assert.That(Success(await client.ReadStreamAsync(new(new(partition, Events, "o1")), cancellationToken)).Events).HasSingleItem();
            await Assert.That(Success(await client.ReadEventSourceAsync(new(subscription.Source), cancellationToken)).Events.Length).IsEqualTo(3);
        }
    }

    private static async Task<SiloRestartRunState> RestartSelectedSiloAsync(ClusterFixture fixture, SiloRestartRunState state,
        HashSet<string> stoppedProcesses, CancellationToken cancellationToken)
    {
        var statuses = (await Task.WhenAll(state.Clients.Select(client => client.StatusAsync(cancellationToken)))).Select(Success).ToArray();
        await Assert.That(statuses.All(status => status.SiloCount == NodeCount && status.ReplicaCount == NodeCount)).IsTrue();
        await Assert.That(statuses.Select(status => status.NodeId).Distinct(StringComparer.Ordinal).Count()).IsEqualTo(NodeCount);
        var selectedIndex = NodeCount - 1;
        var selectedNode = NodeName(selectedIndex + 1);
        stoppedProcesses.Add(selectedNode);
        await fixture.KillServerAsync(selectedNode,
            SiloFailure, cancellationToken);
        var survivors = Enumerable.Range(0, NodeCount).Where(index => index != selectedIndex).ToArray();
        var surviving = state.Clients[survivors[0]];
        await EventuallyAsync(async () => (await surviving.StatusAsync(cancellationToken)).IsSuccess, cancellationToken);
        await VerifyInitialReplicasAsync(survivors.Select(index => state.Clients[index]).ToArray(),
            state.Partition, state.Subscription, cancellationToken);
        await MissingCopiesAsync(await surviving.CommitAsync(state.Command, cancellationToken));
        await fixture.RestartServerAsync(selectedNode, cancellationToken);
        await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(selectedNode, cancellationToken);
        stoppedProcesses.Remove(selectedNode);
        return state with
        {
            Survivors = survivors
        };
    }

    private static async Task RestoreFailedClusterAsync(ClusterFixture fixture, KeyLoadClient[]? clients,
        IEnumerable<string> stoppedProcesses, Exception failure)
    {
        using var recoveryTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(45), TimeProvider.System);
        foreach (var node in stoppedProcesses.Order(StringComparer.Ordinal))
        {
            try
            {
                await fixture.RestartServerAsync(node, recoveryTimeout.Token);
            }
            catch (Exception restartFailure) when (IsNonFatalCleanupFailure(restartFailure))
            {
                failure.Data[ClusterRestartFailureKeyPrefix + node] = restartFailure;
            }
        }

        if (clients is null)
        {
            return;
        }
        foreach (var client in clients)
        {
            try
            {
                await EventuallyAsync(async () =>
                {
                    var status = await client.StatusAsync(recoveryTimeout.Token);
                    return status.IsSuccess && status.Value.RoutingReady;
                }, recoveryTimeout.Token);
            }
            catch (Exception readinessFailure) when (IsNonFatalCleanupFailure(readinessFailure))
            {
                failure.Data[ClusterReadinessFailureKey] = readinessFailure;
            }
        }
    }

    private static async Task<ProjectionBatch> ProcessSubscriptionAsync(SiloRestartRunState state, CancellationToken cancellationToken)
    {
        var surviving = state.Clients[state.Survivors[0]];
        var subscriptionReceive = new ReceiveSubscriptionRequest(Guid.NewGuid(), state.Subscription, MaxEvents: 3, LeaseSeconds: 120);
        var groupEvents = Success(await RetryDuringSiloRecoveryAsync(() => surviving.ReceiveSubscriptionAsync(subscriptionReceive, cancellationToken),
            cancellationToken)).Deliveries;
        await Assert.That(groupEvents.Length).IsEqualTo(3);
        foreach (var deliveryIndex in new[] { 0, 2 })
        {
            var acknowledgement = new SubscriptionDeliveryCommand(Guid.NewGuid(), state.Subscription,
                groupEvents[deliveryIndex].Token, DeliveryAction.Ack);
            Success(await RetryDuringSiloRecoveryAsync(() => surviving.CompleteSubscriptionAsync(acknowledgement, cancellationToken), cancellationToken));
        }

        await Assert.That(Success(await surviving.SubscriptionStatusAsync(state.Subscription, cancellationToken)).Checkpoint).IsEqualTo(1);
        var groupProcessing = new SubscriptionProcessingRequest(Guid.NewGuid(), state.Subscription, groupEvents[1].Token, "projection", 1,
            [new PatchDocument(Collection, "o1", [new("/projected", PatchKind.Set, "true")], 2)]);
        var projected = Success(await RetryDuringSiloRecoveryAsync(() => surviving.CommitSubscriptionProcessingAsync(groupProcessing, cancellationToken),
            cancellationToken));
        var replay = groupProcessing with
        {
            CommandId = Guid.NewGuid()
        };
        var replayed = Success(await RetryDuringSiloRecoveryAsync(() => surviving.CommitSubscriptionProcessingAsync(replay, cancellationToken),
            cancellationToken));
        await Assert.That(replayed.AlreadyProcessed).IsTrue();
        await Assert.That(replayed.OriginalEffectsToken).IsEqualTo(projected.OriginalEffectsToken);
        await Assert.That(Success(await state.Clients[state.Survivors[1]].SubscriptionStatusAsync(state.Subscription, cancellationToken)).Checkpoint)
            .IsEqualTo(3);
        var liveChanges = Success(await state.Clients[state.Survivors[1]].ReadLiveQueryAsync(
            new(state.LiveRequest, state.LiveSnapshot.Cursor), cancellationToken));
        await Assert.That(liveChanges.Changes.Select(change => change.Revision))
            .IsEquivalentTo(new long[] { 2, 3 }, CollectionOrdering.Matching);
        var projectionBatch = Success(await state.Clients[state.Survivors[1]].ReadProjectionAsync(new(state.ProjectionConsumer), cancellationToken));
        await Assert.That(projectionBatch.Entries.Length).IsEqualTo(3);
        return projectionBatch;
    }

    private static async Task<long> CommitProjectionAsync(SiloRestartRunState state, ProjectionBatch projectionBatch,
        CancellationToken cancellationToken)
    {
        var surviving = state.Clients[state.Survivors[0]];
        var projectionRequest = new CommitProjectionBatchRequest(Guid.NewGuid(), state.ProjectionConsumer, projectionBatch.Token,
            [new PutDocument(Materialized, "o1", "{\"projected\":true}", 0)]);
        var outboxEffect = Success(await RetryDuringSiloRecoveryAsync(() => surviving.CommitProjectionAsync(projectionRequest, cancellationToken),
            cancellationToken));
        var replay = projectionRequest with
        {
            CommandId = Guid.NewGuid()
        };
        var duplicateEffect = Success(await RetryDuringSiloRecoveryAsync(() => surviving.CommitProjectionAsync(replay, cancellationToken),
            cancellationToken));
        await Assert.That(duplicateEffect.AlreadyProcessed).IsTrue();
        await Assert.That(duplicateEffect.Receipt.Token).IsEqualTo(outboxEffect.Receipt.Token);
        return projectionBatch.ThroughSequence;
    }

}
