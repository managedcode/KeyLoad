using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal static class ClusterRestoreRf3EventingHealthy
{
    private const string PublishKind = "publishTopic";
    private const string EnqueueKind = "enqueue";
    private const string EmptyHeaders = "{}";
    private const long AckedStateVersion = 3;

    internal static async Task RunAsync(KeyLoadClient sdk, McpOfficialClient official,
        ClusterRestoreRf3EventingState state, PhysicalShardRecord owner, CancellationToken cancellationToken)
    {
        _ = await McpCallerAssertions.SdkSuccessAsync(await sdk.ConfigureResourceAsync(Guid.NewGuid(),
            new(state.Partition.TenantId, state.Partition.DatabaseId, new(ClusterRestoreRf3EventingProtocol.HealthyQueue,
                ResourceKind.WorkQueue, state.Partition.TransactionDomainId)), cancellationToken).ConfigureAwait(false));
        var command = new CommandRequest(Guid.NewGuid(), state.Partition,
            [new PublishTopic(ClusterRestoreRf3EventingProtocol.Topic,
                [new(ClusterRestoreRf3EventingProtocol.Healthy, ClusterRestoreRf3EventingProtocol.EventType, ClusterRestoreRf3EventingProtocol.Json)]),
                new EnqueueMessage(ClusterRestoreRf3EventingProtocol.HealthyQueue,
                    ClusterRestoreRf3EventingProtocol.Healthy, ClusterRestoreRf3EventingProtocol.Json)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command,
            cancellationToken).ConfigureAwait(false));
        await Assert.That(receipt.Token.Position).IsGreaterThan(state.CurrentCommitPosition);
        await SqlRf3Protocol.EqualAsync(new CommitReceipt(command.CommandId, new(owner.Incarnation,
            state.Partition.AtomicPartitionId, receipt.Token.Position, owner.PlacementEpoch),
            [new(PublishKind, ClusterRestoreRf3EventingProtocol.Topic, ClusterRestoreRf3EventingProtocol.Healthy,
                ClusterRestoreRf3EventingProtocol.HealthyPosition),
                new(EnqueueKind, ClusterRestoreRf3EventingProtocol.HealthyQueue, ClusterRestoreRf3EventingProtocol.Healthy,
                    ClusterRestoreRf3EventingProtocol.FirstRevision)], DurabilityProfile.QuorumProcessDurable), receipt);
        state.CurrentCommitPosition = receipt.Token.Position;
        await ClusterRestoreRf3EventingReadOracle.FourAsync(receipt, receipt, sdk, official, state.Partition,
            McpCallerTools.DocumentsCommit, command, cancellationToken).ConfigureAwait(false);
        var request = new ReceiveSubscriptionRequest(Guid.NewGuid(), state.Group);
        var next = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveSubscriptionAsync(request,
            cancellationToken).ConfigureAwait(false));
        await Assert.That(next.Deliveries.Length).IsEqualTo(ClusterRestoreRf3EventingProtocol.SingleDelivery);
        var actual = next.Deliveries[ClusterRestoreRf3EventingProtocol.FirstIndex].Event;
        await Assert.That(actual.EventSequence).IsGreaterThan(state.Page.Events[^ClusterRestoreRf3EventingProtocol.SingleDelivery].EventSequence);
        await SqlRf3Protocol.EqualAsync(new SourceEventRecord(state.Source, ClusterRestoreRf3EventingProtocol.HealthyPosition,
            actual.EventSequence, new(ClusterRestoreRf3EventingProtocol.Healthy, ClusterRestoreRf3EventingProtocol.EventType,
                ClusterRestoreRf3EventingProtocol.Json), actual.RecordedAt), actual);
        state.FinalEvents = [.. state.Page.Events, actual];
        await ClusterRestoreRf3EventingReceiveOracle.GroupAsync(request, state, owner, next, healthy: true);
        await ClusterRestoreRf3EventingReadOracle.FourAsync(next, next, sdk, official, state.Partition,
            McpCallerTools.SubscriptionsReceive, request, cancellationToken).ConfigureAwait(false);
        var ack = new SubscriptionDeliveryCommand(Guid.NewGuid(), state.Group,
            next.Deliveries[ClusterRestoreRf3EventingProtocol.FirstIndex].Token, DeliveryAction.Ack);
        var acknowledged = await McpCallerAssertions.SdkSuccessAsync(await sdk.CompleteSubscriptionAsync(ack,
            cancellationToken).ConfigureAwait(false));
        await ClusterRestoreRf3EventingReceiptOracle.GroupAckAsync(ack.CommandId, state, owner,
            ClusterRestoreRf3EventingProtocol.HealthyPosition,
            next.Deliveries[ClusterRestoreRf3EventingProtocol.FirstIndex].LeaseVersion, acknowledged);
        await ClusterRestoreRf3EventingReadOracle.FourAsync(acknowledged, acknowledged, sdk, official, state.Partition,
            McpCallerTools.SubscriptionsComplete, ack, cancellationToken).ConfigureAwait(false);
        await QueueAsync(sdk, official, state, owner, cancellationToken).ConfigureAwait(false);
    }

    private static async Task QueueAsync(KeyLoadClient sdk, McpOfficialClient official,
        ClusterRestoreRf3EventingState state, PhysicalShardRecord owner, CancellationToken cancellationToken)
    {
        var lane = new QueueLaneRef(state.Partition, ClusterRestoreRf3EventingProtocol.HealthyQueue);
        var request = new ReceiveRequest(Guid.NewGuid(), lane);
        var received = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveAsync(request,
            cancellationToken).ConfigureAwait(false));
        await Assert.That(received.Deliveries.Length).IsEqualTo(ClusterRestoreRf3EventingProtocol.SingleDelivery);
        var actual = received.Deliveries[ClusterRestoreRf3EventingProtocol.FirstIndex];
        await Assert.That(actual.Token).IsNotEmpty();
        var expected = new Delivery(ClusterRestoreRf3EventingProtocol.Healthy,
            ClusterRestoreRf3EventingProtocol.Json, EmptyHeaders, actual.Token,
            ClusterRestoreRf3EventingProtocol.InitialGeneration, actual.LeaseUntil,
            ClusterRestoreRf3EventingProtocol.SingleDelivery, ClusterRestoreRf3EventingProtocol.InitialGeneration);
        await SqlRf3Protocol.EqualAsync(expected, actual);
        await ClusterRestoreRf3EventingReceiveOracle.QueueAsync(request, state, owner, received, expected);
        await ClusterRestoreRf3EventingReadOracle.FourAsync(received, received, sdk, official, state.Partition,
            McpCallerTools.MessagesReceive, request, cancellationToken).ConfigureAwait(false);
        var ack = new DeliveryCommand(Guid.NewGuid(), lane, actual.Token, DeliveryAction.Ack);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CompleteAsync(ack,
            cancellationToken).ConfigureAwait(false));
        await ClusterRestoreRf3EventingReadOracle.FourAsync(receipt, receipt, sdk, official, state.Partition,
            McpCallerTools.MessagesComplete, ack, cancellationToken).ConfigureAwait(false);
        state.HealthyMessage = new MessageInspection(new(ClusterRestoreRf3EventingProtocol.Healthy,
            MessageState.Acked, ClusterRestoreRf3EventingProtocol.SingleDelivery, AckedStateVersion,
            ClusterRestoreRf3EventingProtocol.FirstPosition, null, null,
            LeaseVersion: ClusterRestoreRf3EventingProtocol.InitialGeneration), null, null);
        await ClusterRestoreRf3EventingReceiptOracle.QueueAckAsync(ack.CommandId, state, owner, receipt, state.HealthyMessage);
        var inspect = new InspectMessageRequest(lane, ClusterRestoreRf3EventingProtocol.Healthy);
        await ClusterRestoreRf3EventingReadOracle.FourAsync<MessageInspection?>(state.HealthyMessage,
            await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(inspect, cancellationToken).ConfigureAwait(false)),
            sdk, official, state.Partition, McpCallerTools.MessagesInspect, inspect, cancellationToken).ConfigureAwait(false);
    }
}
