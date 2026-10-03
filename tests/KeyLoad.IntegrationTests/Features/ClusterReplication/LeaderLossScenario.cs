using KeyLoad.Client;
using KeyLoad.Query;
using TUnit.Assertions.Enums;
using static KeyLoad.IntegrationTests.Features.ClusterReplication.ClusterReplicationTestSupport;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class LeaderLossScenario
{
    private const int NodeCount = 3;
    private const string PartitionKeyFormat = "N";
    private const string LeadershipFailure = "leader-election-minority";
    private const string Collection = "orders";
    private const string Events = "events";
    private const string Jobs = "jobs";
    private const string Activity = "activity";
    private const string Materialized = "materialized";

    internal static async Task RunAsync(ClusterFixture fixture)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var stoppedContainers = new HashSet<string>(StringComparer.Ordinal);
        LeaderLossRunState? state = null;
        try
        {
            state = await PrepareAsync(fixture, timeout.Token);
            state = await RemoveLeaderAsync(fixture, state, stoppedContainers, timeout.Token);
            await LeaderLossQueueScenario.ProcessAsync(state, Collection, Jobs, timeout.Token);
            var projectionBatch = await ProcessSubscriptionAsync(state, timeout.Token);
            var projectionThroughSequence = await CommitProjectionAsync(state, projectionBatch, timeout.Token);
            await LeaderLossRecoveryScenario.VerifyMinorityAndRecoveryAsync(
                fixture, state, projectionThroughSequence, stoppedContainers, timeout.Token);
        }
        catch (Exception failure) when (IsNonFatalCleanupFailure(failure))
        {
            await SaveFailureDiagnosticsAsync(fixture, failure);
            await RestoreFailedClusterAsync(fixture, state?.Clients, stoppedContainers, failure);
            throw;
        }
    }

    private static async Task<LeaderLossRunState> PrepareAsync(ClusterFixture fixture, CancellationToken cancellationToken)
    {
        var clients = Enumerable.Range(1, NodeCount).Select(number => fixture.Client(NodeName(number))).ToArray();
        var partition = new PartitionRef("integration", "database", Collection, Guid.NewGuid().ToString(PartitionKeyFormat));
        var resources = await ConfigureResourcesAsync(clients, partition, cancellationToken);
        var command = await CommitInitialBatchAsync(clients, partition, cancellationToken);
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
            new(Collection, ResourceKind.Collection, Collection) { Indexes = [new("number", ["/number"], true)] }), cancellationToken));
        Success(await clients[1].ConfigureResourceAsync(Guid.NewGuid(), new(partition.TenantId, partition.DatabaseId,
            new(Events, ResourceKind.StreamSet, Collection)), cancellationToken));
        Success(await clients[2].ConfigureResourceAsync(Guid.NewGuid(), new(partition.TenantId, partition.DatabaseId,
            new(Jobs, ResourceKind.WorkQueue, Collection)), cancellationToken));
        Success(await clients[0].ConfigureResourceAsync(Guid.NewGuid(), new(partition.TenantId, partition.DatabaseId,
            new(Activity, ResourceKind.Topic, Collection)), cancellationToken));
        Success(await clients[0].ConfigureResourceAsync(Guid.NewGuid(), new(partition.TenantId, partition.DatabaseId,
            new(Materialized, ResourceKind.Collection, Collection)), cancellationToken));
        var projectionConsumer = new ProjectionConsumerRef(partition, "outbox-v1");
        Success(await clients[0].ConfigureProjectionAsync(new(Guid.NewGuid(), projectionConsumer, new(1, [Collection], [])), cancellationToken));
        var subscription = new SubscriptionRef(new(partition, Activity, EventSourceKind.Topic), "projection");
        Success(await clients[1].ConfigureSubscriptionAsync(new(Guid.NewGuid(), subscription, new("root")), cancellationToken));
        return (projectionConsumer, subscription);
    }

    private static async Task<InitialCommand> CommitInitialBatchAsync(KeyLoadClient[] clients, PartitionRef partition,
        CancellationToken cancellationToken)
    {
        var command = new CommandRequest(Guid.NewGuid(), partition,
        [
            new PutDocument(Collection, "o1", "{\"number\":1}", 0),
            new AppendEvents(Events, "o1", [new("e1", "Created", "{}")], ExpectedStreamRevision.NoStream),
            new EnqueueMessage(Jobs, "m1", "{}"),
            new PublishTopic(Activity, [new("t1", "Created", "{}"), new("t2", "Updated", "{}"), new("t3", "Updated", "{}")])
        ]);
        var committed = Success(await clients[2].CommitAsync(command, cancellationToken));
        await Assert.That(committed.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        return new(command, committed);
    }

    private static async Task VerifyInitialReplicasAsync(KeyLoadClient[] clients, PartitionRef partition,
        SubscriptionRef subscription, CancellationToken cancellationToken)
    {
        foreach (var client in clients)
        {
            await Assert.That(Success(await client.GetAsync(new(partition, Collection, "o1"), cancellationToken))!.Revision).IsEqualTo(1);
            await Assert.That(Success(await client.ReadStreamAsync(new(new(partition, Events, "o1")), cancellationToken)).Events).HasSingleItem();
            await Assert.That(Success(await client.ReadEventSourceAsync(new(subscription.Source), cancellationToken)).Events.Length).IsEqualTo(3);
        }
    }

    private static async Task<LeaderLossRunState> RemoveLeaderAsync(ClusterFixture fixture, LeaderLossRunState state,
        HashSet<string> stoppedContainers, CancellationToken cancellationToken)
    {
        var statuses = (await Task.WhenAll(state.Clients.Select(client => client.StatusAsync(cancellationToken)))).Select(Success).ToArray();
        var leader = new Uri(statuses[0].Leader!);
        var leaderIndex = Enumerable.Range(0, NodeCount).Single(index => string.Equals(leader.Host,
            NodeName(index + 1), StringComparison.Ordinal));
        var leaderNode = NodeName(leaderIndex + 1);
        stoppedContainers.Add(leaderNode);
        await fixture.KillContainerAsync(leaderNode,
            LeadershipFailure, cancellationToken);
        var survivors = Enumerable.Range(0, NodeCount).Where(index => index != leaderIndex).ToArray();
        var surviving = state.Clients[survivors[0]];
        await EventuallyAsync(async () => (await surviving.StatusAsync(cancellationToken)).IsSuccess, cancellationToken);
        return state with { Survivors = survivors };
    }

    private static async Task SaveFailureDiagnosticsAsync(ClusterFixture fixture, Exception failure)
    {
        try
        { await fixture.SaveFailureDiagnosticsAsync(); }
        catch (Exception diagnosticsFailure) when (IsNonFatalCleanupFailure(diagnosticsFailure))
        { failure.Data[Rf3DiagnosticsFailureKey] = diagnosticsFailure; }
    }

    private static async Task RestoreFailedClusterAsync(ClusterFixture fixture, KeyLoadClient[]? clients,
        IEnumerable<string> stoppedContainers, Exception failure)
    {
        using var recoveryTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        foreach (var node in stoppedContainers.Order(StringComparer.Ordinal))
        {
            try
            { await fixture.RestartContainerAsync(node, recoveryTimeout.Token); }
            catch (Exception restartFailure) when (IsNonFatalCleanupFailure(restartFailure))
            { failure.Data[Rf3RestartFailureKeyPrefix + node] = restartFailure; }
        }

        if (clients is null)
        { return; }
        foreach (var client in clients)
        {
            try
            {
                await EventuallyAsync(async () =>
                {
                    var status = await client.StatusAsync(recoveryTimeout.Token);
                    return status.IsSuccess && status.Value!.RoutingReady;
                }, recoveryTimeout.Token);
            }
            catch (Exception readinessFailure) when (IsNonFatalCleanupFailure(readinessFailure))
            { failure.Data[Rf3ReadinessFailureKey] = readinessFailure; }
        }
    }

    private static async Task<ProjectionBatch> ProcessSubscriptionAsync(LeaderLossRunState state, CancellationToken cancellationToken)
    {
        var surviving = state.Clients[state.Survivors[0]];
        var subscriptionReceive = new ReceiveSubscriptionRequest(Guid.NewGuid(), state.Subscription, MaxEvents: 3, LeaseSeconds: 120);
        var groupEvents = Success(await RetryDuringElectionAsync(() => surviving.ReceiveSubscriptionAsync(subscriptionReceive, cancellationToken),
            cancellationToken)).Deliveries;
        await Assert.That(groupEvents.Length).IsEqualTo(3);
        foreach (var deliveryIndex in new[] { 0, 2 })
        {
            var acknowledgement = new SubscriptionDeliveryCommand(Guid.NewGuid(), state.Subscription,
                groupEvents[deliveryIndex].Token, DeliveryAction.Ack);
            Success(await RetryDuringElectionAsync(() => surviving.CompleteSubscriptionAsync(acknowledgement, cancellationToken), cancellationToken));
        }

        await Assert.That(Success(await surviving.SubscriptionStatusAsync(state.Subscription, cancellationToken)).Checkpoint).IsEqualTo(1);
        var groupProcessing = new SubscriptionProcessingRequest(Guid.NewGuid(), state.Subscription, groupEvents[1].Token, "projection", 1,
            [new PatchDocument(Collection, "o1", [new("/projected", PatchKind.Set, "true")], 2)]);
        var projected = Success(await RetryDuringElectionAsync(() => surviving.CommitSubscriptionProcessingAsync(groupProcessing, cancellationToken),
            cancellationToken));
        var replay = groupProcessing with { CommandId = Guid.NewGuid() };
        var replayed = Success(await RetryDuringElectionAsync(() => surviving.CommitSubscriptionProcessingAsync(replay, cancellationToken),
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

    private static async Task<long> CommitProjectionAsync(LeaderLossRunState state, ProjectionBatch projectionBatch,
        CancellationToken cancellationToken)
    {
        var surviving = state.Clients[state.Survivors[0]];
        var projectionRequest = new CommitProjectionBatchRequest(Guid.NewGuid(), state.ProjectionConsumer, projectionBatch.Token,
            [new PutDocument(Materialized, "o1", "{\"projected\":true}", 0)]);
        var outboxEffect = Success(await RetryDuringElectionAsync(() => surviving.CommitProjectionAsync(projectionRequest, cancellationToken),
            cancellationToken));
        var replay = projectionRequest with { CommandId = Guid.NewGuid() };
        var duplicateEffect = Success(await RetryDuringElectionAsync(() => surviving.CommitProjectionAsync(replay, cancellationToken),
            cancellationToken));
        await Assert.That(duplicateEffect.AlreadyProcessed).IsTrue();
        await Assert.That(duplicateEffect.Receipt.Token).IsEqualTo(outboxEffect.Receipt.Token);
        return projectionBatch.ThroughSequence;
    }

    private sealed record InitialCommand(CommandRequest Command, CommitReceipt Receipt);
}
