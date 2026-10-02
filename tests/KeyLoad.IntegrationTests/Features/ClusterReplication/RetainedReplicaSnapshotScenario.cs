using KeyLoad.Client;
using static KeyLoad.IntegrationTests.Features.ClusterReplication.ClusterReplicationTestSupport;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class RetainedReplicaSnapshotScenario
{
    private const int NodeCount = 3;
    private const int DocumentCount = 40;
    private const string SnapshotDirectory = "snapshots";
    private const string Activity = "snapshot-activity";
    private const string Projection = "snapshot-outbox-v1";

    internal static async Task RunAsync(ClusterFixture fixture)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try
        {
            var state = await ConfigureAndProduceAsync(fixture, timeout.Token);
            var replica = await StopRetainedReplicaAsync(fixture, state, timeout.Token);
            var final = await ReplicateWhileStoppedAsync(fixture, state, replica.Index, timeout.Token);
            await RestartAndVerifyAsync(fixture, state, replica, final, timeout.Token);
        }
        catch (Exception)
        {
            await fixture.SaveFailureDiagnosticsAsync();
            throw;
        }
    }

    private static async Task<SnapshotState> ConfigureAndProduceAsync(ClusterFixture fixture,
        CancellationToken cancellationToken)
    {
        var clients = Enumerable.Range(1, NodeCount).Select(number => fixture.Client(NodeName(number))).ToArray();
        var partition = new PartitionRef("integration", "database", "snapshot", Guid.NewGuid().ToString("N"));
        var configuration = await ConfigureAsync(clients[0], partition, cancellationToken);
        var processing = await ProduceEffectsAsync(clients, partition, configuration, cancellationToken);
        var batch = Success(await clients[0].ReadProjectionAsync(new(configuration.Consumer), cancellationToken));
        var projectionRequest = new CommitProjectionBatchRequest(Guid.NewGuid(), configuration.Consumer, batch.Token, []);
        var outboxEffect = Success(await RetryDuringElectionAsync(
            () => clients[0].CommitProjectionAsync(projectionRequest, cancellationToken), cancellationToken));
        var statuses = (await Task.WhenAll(clients.Select(client => client.StatusAsync(cancellationToken))))
            .Select(Success).ToArray();
        return new(clients, partition, configuration.Subscription, configuration.Consumer, processing.Request,
            processing.Result, batch, projectionRequest, outboxEffect, statuses);
    }

    private static async Task<SnapshotConfiguration> ConfigureAsync(KeyLoadClient client, PartitionRef partition,
        CancellationToken cancellationToken)
    {
        var resource = new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId,
            new(SnapshotDirectory, ResourceKind.Collection, "snapshot"));
        var resourceId = Guid.NewGuid();
        Success(await RetryDuringElectionAsync(() => client.ConfigureResourceAsync(resourceId, resource, cancellationToken), cancellationToken));
        var topic = new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId,
            new(Activity, ResourceKind.Topic, "snapshot"));
        var topicId = Guid.NewGuid();
        Success(await RetryDuringElectionAsync(() => client.ConfigureResourceAsync(topicId, topic, cancellationToken), cancellationToken));
        var subscription = new SubscriptionRef(new(partition, Activity, EventSourceKind.Topic), "projection");
        var group = new ConfigureSubscriptionRequest(Guid.NewGuid(), subscription, new("root"));
        Success(await RetryDuringElectionAsync(() => client.ConfigureSubscriptionAsync(group, cancellationToken), cancellationToken));
        var consumer = new ProjectionConsumerRef(partition, Projection);
        var projection = new ConfigureProjectionConsumerRequest(Guid.NewGuid(), consumer, new(1, [SnapshotDirectory], []));
        Success(await RetryDuringElectionAsync(() => client.ConfigureProjectionAsync(projection, cancellationToken), cancellationToken));
        return new(subscription, consumer);
    }

    private static async Task<(SubscriptionProcessingRequest Request, SubscriptionProcessingResult Result)> ProduceEffectsAsync(KeyLoadClient[] clients,
        PartitionRef partition, SnapshotConfiguration configuration, CancellationToken cancellationToken)
    {
        var producer = new CommandRequest(Guid.NewGuid(), partition,
            [new PublishTopic(Activity, [new("input", "Created", "{}")])]);
        Success(await RetryDuringElectionAsync(() => clients[1].CommitAsync(producer, cancellationToken), cancellationToken));
        var receive = new ReceiveSubscriptionRequest(Guid.NewGuid(), configuration.Subscription);
        var input = await Assert.That(Success(await RetryDuringElectionAsync(
            () => clients[2].ReceiveSubscriptionAsync(receive, cancellationToken), cancellationToken)).Deliveries).HasSingleItem();
        var processing = new SubscriptionProcessingRequest(Guid.NewGuid(), configuration.Subscription, input.Token, "projection", 1,
            [new PutDocument(SnapshotDirectory, "projection", "{\"done\":true}", 0)]);
        var effect = Success(await RetryDuringElectionAsync(() => clients[0].CommitSubscriptionProcessingAsync(processing, cancellationToken),
            cancellationToken));
        return (processing, effect);
    }

    private static async Task<StoppedReplica> StopRetainedReplicaAsync(ClusterFixture fixture, SnapshotState state,
        CancellationToken cancellationToken)
    {
        var leaderHost = new Uri(state.InitialStatuses[0].Leader!).Host;
        var index = Enumerable.Range(0, NodeCount).First(candidate =>
            !string.Equals(leaderHost, NodeName(candidate + 1), StringComparison.Ordinal));
        var name = NodeName(index + 1);
        var directory = Path.Combine(fixture.Root, name);
        await Assert.That(Directory.Exists(directory)).IsTrue();
        await fixture.KillContainerAsync(name, "retained-replica-restart", cancellationToken);
        return new(index, name, directory);
    }

    private static async Task<FinalCommand> ReplicateWhileStoppedAsync(ClusterFixture fixture, SnapshotState state,
        int stoppedIndex, CancellationToken cancellationToken)
    {
        var availableNodes = Enumerable.Range(0, state.Clients.Length).Where(index => index != stoppedIndex).ToArray();
        CommandRequest? last = null;
        CommitReceipt? committed = null;
        foreach (var index in Enumerable.Range(0, DocumentCount))
        {
            var command = new CommandRequest(Guid.NewGuid(), state.Partition,
                [new PutDocument(SnapshotDirectory, "doc-" + index, "{\"n\":" + index + "}", 0)]);
            last = command;
            var client = state.Clients[availableNodes[index % availableNodes.Length]];
            committed = Success(await RetryDuringElectionAsync(() => client.CommitAsync(command, cancellationToken), cancellationToken));
        }

        foreach (var number in Enumerable.Range(1, NodeCount).Where(number => number != stoppedIndex + 1))
        {
            await EventuallyAsync(() => Task.FromResult(Directory.EnumerateFiles(
                Path.Combine(fixture.Root, NodeName(number), SnapshotDirectory), "*.snapshot").Any()), cancellationToken);
        }

        return new(last!, committed!);
    }

    private static async Task RestartAndVerifyAsync(ClusterFixture fixture, SnapshotState state, StoppedReplica replica,
        FinalCommand final, CancellationToken cancellationToken)
    {
        await fixture.RestartContainerAsync(replica.Name, cancellationToken);
        await Assert.That(Directory.Exists(replica.Directory)).IsTrue();
        await EventuallyAsync(async () =>
        {
            var status = await state.Clients[replica.Index].StatusAsync(cancellationToken);
            return status.IsSuccess && status.Value!.RoutingReady;
        }, cancellationToken);
        await VerifyRecoveredReplicaAsync(state, replica.Index, final, cancellationToken);
    }

    private static async Task VerifyRecoveredReplicaAsync(SnapshotState state, int index, FinalCommand final,
        CancellationToken cancellationToken)
    {
        var client = state.Clients[index];
        var recovered = Success(await client.StatusAsync(cancellationToken));
        await Assert.That(recovered.NodeId).IsEqualTo(state.InitialStatuses[index].NodeId);
        await Assert.That(recovered.Incarnation).IsEqualTo(state.InitialStatuses[index].Incarnation);
        await Assert.That(Success(await client.GetAsync(new(state.Partition, SnapshotDirectory, "doc-39"), cancellationToken))!.Revision).IsEqualTo(1);
        await Assert.That(Success(await RetryDuringElectionAsync(() => client.CommitAsync(final.Command, cancellationToken),
            cancellationToken)).Token).IsEqualTo(final.Receipt.Token);
        await Assert.That(recovered.ReadGeneration > state.InitialStatuses[index].ReadGeneration).IsTrue()
            .Because("The retained replica must advance its installed snapshot generation while catching up.");
        await Assert.That(Success(await client.SubscriptionStatusAsync(state.Subscription, cancellationToken)).Checkpoint).IsEqualTo(1);
        await Assert.That(Success(await client.GetAsync(new(state.Partition, SnapshotDirectory, "projection"), cancellationToken))!.Revision).IsEqualTo(1);
        await Assert.That(Success(await client.OutboxStatusAsync(state.Partition, cancellationToken)).Consumers.Single().Checkpoint)
            .IsEqualTo(state.Batch.ThroughSequence);
        await VerifyReplayAndHistoryAsync(state, client, cancellationToken);
    }

    private static async Task VerifyReplayAndHistoryAsync(SnapshotState state, KeyLoadClient client,
        CancellationToken cancellationToken)
    {
        var outboxReplay = Success(await client.CommitProjectionAsync(
            state.ProjectionRequest with { CommandId = Guid.NewGuid() }, cancellationToken));
        await Assert.That(outboxReplay.AlreadyProcessed).IsTrue();
        await Assert.That(outboxReplay.Receipt.Token).IsEqualTo(state.OutboxEffect.Receipt.Token);
        await Assert.That(Success(await client.ReadChangesAsync(new(state.Partition, SnapshotDirectory), cancellationToken)).Changes.Length)
            .IsEqualTo(DocumentCount + 1);
        await Assert.That(Success(await client.ReadEventSourceAsync(new(state.Subscription.Source), cancellationToken)).Events)
            .HasSingleItem();
        var replay = state.Processing with { CommandId = Guid.NewGuid() };
        var replayed = Success(await RetryDuringElectionAsync(() => client.CommitSubscriptionProcessingAsync(replay, cancellationToken),
            cancellationToken));
        await Assert.That(replayed.AlreadyProcessed).IsTrue();
        await Assert.That(replayed.OriginalEffectsToken).IsEqualTo(state.ProcessingEffect.OriginalEffectsToken);
        await Assert.That(Success(await client.GetAsync(new(state.Partition, SnapshotDirectory, "projection"), cancellationToken))!.Revision)
            .IsEqualTo(1);
    }

    private sealed record SnapshotConfiguration(SubscriptionRef Subscription, ProjectionConsumerRef Consumer);
    private sealed record StoppedReplica(int Index, string Name, string Directory);
    private sealed record FinalCommand(CommandRequest Command, CommitReceipt Receipt);
    private sealed record SnapshotState(KeyLoadClient[] Clients, PartitionRef Partition, SubscriptionRef Subscription,
        ProjectionConsumerRef Consumer, SubscriptionProcessingRequest Processing, SubscriptionProcessingResult ProcessingEffect,
        ProjectionBatch Batch, CommitProjectionBatchRequest ProjectionRequest, ProjectionBatchResult OutboxEffect,
        NodeStatus[] InitialStatuses);
}
