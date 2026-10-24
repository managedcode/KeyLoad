using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;
using TUnit.Assertions.Enums;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class QueueByteSublimitPublicFlow
{
    private static EnqueueMessage Enqueue(string id)
        => new(QueueLifecyclePublicProtocol.Queue, id, QueueLifecyclePublicProtocol.Payload,
            QueueLifecyclePublicProtocol.Headers, OrderingKey: QueueLifecyclePublicProtocol.Ordering);

    internal static async Task RunAsync(ClusterFixture fixture, bool mcpFirst)
    {
        using var deadline = McpCallerDeadline.Create();
        var owner = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            root => QueueLifecyclePublicSeed.RunAsync(root,
                mcpFirst ? QueueLifecyclePublicProtocol.Mcp : QueueLifecyclePublicProtocol.Sdk, true, deadline.Token),
            deadline.Token);
        var state = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, owner.OperatorKey,
            callers => StartAsync(callers, owner, deadline.Token), deadline.Token);
        await ModelSqlClusterCold.RunAsync(fixture, deadline.Token);
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3, owner.OperatorKey,
            callers => FirstColdAsync(callers, state, deadline.Token), deadline.Token);
        await ModelSqlClusterCold.RunAsync(fixture, deadline.Token);
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, owner.OperatorKey,
            callers => SecondColdAsync(callers, state, deadline.Token), deadline.Token);
    }

    private static async Task<QueueByteSublimitPublicState> StartAsync(RequestCqrsClusterCallers callers,
        QueueLifecyclePublicState owner, CancellationToken token)
    {
        await QueueLifecyclePublicRoutes.CommitAsync(callers, owner, new(Guid.NewGuid(), owner.Partition,
            [new PutDocument(QueueLifecyclePublicProtocol.Collection, QueueByteSublimitPublicAssertions.Witness,
                QueueLifecyclePublicProtocol.Payload), Enqueue(QueueByteSublimitPublicAssertions.First),
                Enqueue(QueueByteSublimitPublicAssertions.Second)]), token);
        var request = new ReceiveRequest(Guid.NewGuid(), owner.Lane, LeaseSeconds: new QueuePolicy().MaxLeaseSeconds);
        var claim = await QueueLifecyclePublicRoutes.CallAsync(callers, owner.Route, owner.Partition,
            McpCallerTools.MessagesReceive, request, () => callers.Sdk.ReceiveAsync(request, token), token);
        var delivery = await Assert.That(claim.Deliveries).HasSingleItem();
        await Assert.That(delivery.Id).IsEqualTo(QueueByteSublimitPublicAssertions.First);
        await Assert.That(delivery.Attempt).IsEqualTo(1);
        await Assert.That(delivery.LeaseVersion).IsEqualTo(1L);
        await Assert.That(delivery.DeliveryGeneration).IsEqualTo(1L);
        await Assert.That(delivery.PayloadJson).IsEqualTo(QueueLifecyclePublicProtocol.CanonicalPayload);
        await Assert.That(delivery.HeadersJson).IsEqualTo(QueueLifecyclePublicProtocol.Headers);
        await QueueLeaseClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await callers.Sdk.ReceiveAsync(request, token)), claim);
        var replay = await McpCallerAssertions.SuccessAsync<ReceiveResult>(
            await callers.Mcp.CallAsync(McpCallerTools.MessagesReceive, request, token));
        await QueueLeaseClusterAssertions.EqualAsync(replay.Value, claim);
        var nack = new DeliveryCommand(Guid.NewGuid(), owner.Lane, delivery.Token, DeliveryAction.Nack);
        await CompleteAsync(callers, owner, nack, QueueByteSublimitPublicAssertions.First, 3L, token);
        var state = new QueueByteSublimitPublicState(owner, request, claim, delivery);
        var park = new ParkPendingQueueMessage(owner.Lane.Queue, delivery.Id, 3L, 1L);
        await QueueByteSublimitPublicAssertions.RefuseAsync(callers, state,
            new(Guid.NewGuid(), owner.Partition, [park]), token);
        await QueueByteSublimitPublicAssertions.RefuseAsync(callers, state, new(Guid.NewGuid(), owner.Partition,
            [new PutDocument(QueueLifecyclePublicProtocol.Collection, QueueByteSublimitPublicAssertions.Witness,
                "{\"knowledge\":\"must remain private and rolled back\"}", ExpectedRevision: 1L), park]), token);
        await QueueByteSublimitPublicAssertions.ImageAsync(callers, state,
            QueueByteSublimitPublicAssertions.Pending(), QueueByteSublimitPublicAssertions.ReadySecond(), token);
        await QueueByteSublimitPublicAssertions.HistoryAsync(callers, state, token);
        return state;
    }

    private static async Task<bool> FirstColdAsync(RequestCqrsClusterCallers callers,
        QueueByteSublimitPublicState state, CancellationToken token)
    {
        var owner = state.Owner;
        await QueueByteSublimitPublicAssertions.HistoryAsync(callers, state, token);
        await QueueByteSublimitPublicAssertions.ImageAsync(callers, state,
            QueueByteSublimitPublicAssertions.Pending(), QueueByteSublimitPublicAssertions.ReadySecond(), token);
        var redrive = await QueueLifecyclePublicRoutes.CommitAsync(callers, owner, new(Guid.NewGuid(), owner.Partition,
            [new RedriveQueueMessage(owner.Lane.Queue, QueueByteSublimitPublicAssertions.First, 3L, 1L)]), token);
        await Assert.That(redrive.Mutations).IsEquivalentTo(new MutationReceipt[] {
            new("redriveQueueMessage", owner.Lane.Queue, QueueByteSublimitPublicAssertions.First, 4L) }, CollectionOrdering.Matching);
        await QueueByteSublimitPublicAssertions.ImageAsync(callers, state,
            QueueByteSublimitPublicAssertions.ReadyFirst(), QueueByteSublimitPublicAssertions.ReadySecond(), token);
        var request = new ReceiveRequest(Guid.NewGuid(), owner.Lane, LeaseSeconds: new QueuePolicy().MaxLeaseSeconds);
        var claim = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReceiveAsync(request, token));
        var sibling = await Assert.That(claim.Deliveries).HasSingleItem();
        await Assert.That(sibling.Id).IsEqualTo(QueueByteSublimitPublicAssertions.Second);
        await Assert.That(sibling.PayloadJson).IsEqualTo(QueueLifecyclePublicProtocol.CanonicalPayload);
        await Assert.That(sibling.HeadersJson).IsEqualTo(QueueLifecyclePublicProtocol.Headers);
        var ack = new DeliveryCommand(Guid.NewGuid(), owner.Lane, sibling.Token, DeliveryAction.Ack);
        await CompleteAsync(callers, owner, ack, QueueByteSublimitPublicAssertions.Second, 3L, token);
        await QueueByteSublimitPublicAssertions.HistoryAsync(callers, state, token);
        await QueueByteSublimitPublicAssertions.ImageAsync(callers, state, QueueByteSublimitPublicAssertions.ReadyFirst(),
            new(QueueByteSublimitPublicAssertions.Second, MessageState.Acked, 1, 3L, 2L, null, null, LeaseVersion: 1L), token);
        return true;
    }

    private static async Task<bool> SecondColdAsync(RequestCqrsClusterCallers callers,
        QueueByteSublimitPublicState state, CancellationToken token)
    {
        var second = new MessageMetadata(QueueByteSublimitPublicAssertions.Second, MessageState.Acked,
            1, 3L, 2L, null, null, LeaseVersion: 1L);
        await QueueByteSublimitPublicAssertions.HistoryAsync(callers, state, token);
        await QueueByteSublimitPublicAssertions.ImageAsync(callers, state,
            QueueByteSublimitPublicAssertions.ReadyFirst(), second, token);
        var request = new ReceiveRequest(Guid.NewGuid(), state.Owner.Lane, LeaseSeconds: new QueuePolicy().MaxLeaseSeconds);
        var claim = await McpCallerAssertions.SuccessAsync<ReceiveResult>(
            await callers.Mcp.CallAsync(McpCallerTools.MessagesReceive, request, token));
        var delivery = await Assert.That(claim.Value.Deliveries).HasSingleItem();
        await Assert.That(delivery.Id).IsEqualTo(QueueByteSublimitPublicAssertions.First);
        await Assert.That(delivery.Token).IsNotEqualTo(state.Delivery.Token);
        await Assert.That(delivery.Attempt).IsEqualTo(1);
        await Assert.That(delivery.LeaseVersion).IsEqualTo(3L);
        await Assert.That(delivery.DeliveryGeneration).IsEqualTo(2L);
        await Assert.That(delivery.PayloadJson).IsEqualTo(QueueLifecyclePublicProtocol.CanonicalPayload);
        await Assert.That(delivery.HeadersJson).IsEqualTo(QueueLifecyclePublicProtocol.Headers);
        await QueueLeaseClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await callers.Sdk.ReceiveAsync(request, token)), claim.Value);
        var ack = new DeliveryCommand(Guid.NewGuid(), state.Owner.Lane, delivery.Token, DeliveryAction.Ack);
        await CompleteAsync(callers, state.Owner, ack, QueueByteSublimitPublicAssertions.First, 6L, token);
        await QueueByteSublimitPublicAssertions.ImageAsync(callers, state,
            new(QueueByteSublimitPublicAssertions.First, MessageState.Acked, 1, 6L, 3L, null, null,
                LeaseVersion: 3L, DeliveryGeneration: 2L), second, token);
        await HealthyAsync(callers, state, token);
        return true;
    }

    private static async Task CompleteAsync(RequestCqrsClusterCallers callers, QueueLifecyclePublicState owner,
        DeliveryCommand command, string id, long version, CancellationToken token)
    {
        var receipt = await QueueLifecyclePublicRoutes.CallAsync(callers, owner.Route, owner.Partition,
            McpCallerTools.MessagesComplete, command, () => callers.Sdk.CompleteAsync(command, token), token);
        owner.Deliveries.Add((command, receipt));
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Mutations).IsEquivalentTo(new MutationReceipt[] {
            new(command.Action.ToString(), owner.Lane.Queue, id, version) },
            CollectionOrdering.Matching);
    }

    private static async Task HealthyAsync(RequestCqrsClusterCallers callers,
        QueueByteSublimitPublicState state, CancellationToken token)
    {
        await QueueLifecyclePublicRoutes.CommitAsync(callers, state.Owner, new(Guid.NewGuid(), state.Owner.Partition,
            [Enqueue(QueueByteSublimitPublicAssertions.Healthy)]), token);
        var request = new ReceiveRequest(Guid.NewGuid(), state.Owner.Lane, LeaseSeconds: new QueuePolicy().MaxLeaseSeconds);
        var claim = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReceiveAsync(request, token));
        var delivery = await Assert.That(claim.Deliveries).HasSingleItem();
        await Assert.That(delivery.Id).IsEqualTo(QueueByteSublimitPublicAssertions.Healthy);
        await Assert.That(delivery.Attempt).IsEqualTo(1);
        await Assert.That(delivery.LeaseVersion).IsEqualTo(1L);
        await Assert.That(delivery.DeliveryGeneration).IsEqualTo(1L);
        var replay = await McpCallerAssertions.SuccessAsync<ReceiveResult>(
            await callers.Mcp.CallAsync(McpCallerTools.MessagesReceive, request, token));
        await QueueLeaseClusterAssertions.EqualAsync(replay.Value, claim);
        await Assert.That(delivery.PayloadJson).IsEqualTo(QueueLifecyclePublicProtocol.CanonicalPayload);
        await Assert.That(delivery.HeadersJson).IsEqualTo(QueueLifecyclePublicProtocol.Headers);
        var ack = new DeliveryCommand(Guid.NewGuid(), state.Owner.Lane, delivery.Token, DeliveryAction.Ack);
        var receipt = await QueueLifecyclePublicRoutes.CallAsync(callers, state.Owner.Route, state.Owner.Partition,
            McpCallerTools.MessagesComplete, ack, () => callers.Sdk.CompleteAsync(ack, token), token);
        state.Owner.Deliveries.Add((ack, receipt));
        await Assert.That(receipt.Mutations).IsEquivalentTo(new MutationReceipt[] {
            new("Ack", state.Owner.Lane.Queue, QueueByteSublimitPublicAssertions.Healthy, 3L) }, CollectionOrdering.Matching);
        await SagaTimeoutClusterAssertions.AssertSdkErrorAsync(
            await callers.Sdk.ReceiveAsync(request, token), ErrorCode.StaleLease);
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(
            McpCallerTools.MessagesReceive, request, token), ErrorCode.StaleLease, true);
        await QueueLeasedCancelPublicAssertions.MessageAsync(callers, state.Owner, QueueByteSublimitPublicAssertions.Healthy,
            new(new(QueueByteSublimitPublicAssertions.Healthy, MessageState.Acked, 1, 3L, 4L, null, null,
                LeaseVersion: 1L), null, null), token);
        await QueueByteSublimitPublicAssertions.HistoryAsync(callers, state, token);
        await QueueByteSublimitPublicAssertions.ImageAsync(callers, state,
            new(QueueByteSublimitPublicAssertions.First, MessageState.Acked, 1, 6L, 3L, null, null,
                LeaseVersion: 3L, DeliveryGeneration: 2L),
            new(QueueByteSublimitPublicAssertions.Second, MessageState.Acked, 1, 3L, 2L, null, null,
                LeaseVersion: 1L), token);
        var empty = await McpCallerAssertions.SdkSuccessAsync(
            await callers.Sdk.ReceiveAsync(new(Guid.NewGuid(), state.Owner.Lane), token));
        await Assert.That(empty.Deliveries).IsEmpty();
    }
}
