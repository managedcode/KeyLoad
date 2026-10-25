using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;
using TUnit.Assertions.Enums;

namespace KeyLoad.ClusterTests.Messaging;

internal static class QueueEmptyHistoryPublicFlow
{
    internal static async Task RunAsync(ClusterFixture fixture, bool mcpFirst)
    {
        using var deadline = McpCallerDeadline.Create();
        var owner = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            root => QueueLifecyclePublicSeed.RunAsync(root, mcpFirst ? QueueLifecyclePublicProtocol.Mcp : QueueLifecyclePublicProtocol.Sdk,
                deadline.Token), deadline.Token);
        var state = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, owner.OperatorKey,
            callers => StartAsync(callers, owner, deadline.Token), deadline.Token);
        await ModelSqlClusterCold.RunAsync(fixture, deadline.Token);
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3, owner.OperatorKey,
            callers => FirstColdAsync(callers, state, deadline.Token), deadline.Token);
        await ModelSqlClusterCold.RunAsync(fixture, deadline.Token);
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, owner.OperatorKey,
            callers => SecondColdAsync(callers, state, deadline.Token), deadline.Token);
    }

    private static EnqueueMessage Enqueue(string id)
        => new(QueueLifecyclePublicProtocol.Queue, id, QueueLifecyclePublicProtocol.Payload,
            QueueLifecyclePublicProtocol.Headers, OrderingKey: QueueLifecyclePublicProtocol.Ordering);

    private static async Task<QueueEmptyHistoryPublicState> StartAsync(RequestCqrsClusterCallers callers,
        QueueLifecyclePublicState owner, CancellationToken token)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), owner.Lane, MaxMessages: 1);
        var empty = await QueueLifecyclePublicRoutes.CallAsync(callers, owner.Route, owner.Partition,
            McpCallerTools.MessagesReceive, request, () => callers.Sdk.ReceiveAsync(request, token), token);
        await Assert.That(empty.RequestId).IsEqualTo(request.RequestId);
        await Assert.That(empty.Deliveries).IsEmpty();
        var state = new QueueEmptyHistoryPublicState(owner, request, empty);
        await QueueEmptyHistoryPublicAssertions.ChangedAsync(callers, state, token);
        await QueueEmptyHistoryPublicAssertions.OriginalAsync(callers, state, token);
        await QueueLifecyclePublicRoutes.CommitAsync(callers, owner, new(Guid.NewGuid(), owner.Partition,
            [Enqueue(QueueEmptyHistoryPublicAssertions.First), Enqueue(QueueEmptyHistoryPublicAssertions.Sibling)]), token);
        await QueueEmptyHistoryPublicAssertions.ReadyAsync(callers, state, false, token);
        await QueueEmptyHistoryPublicAssertions.OriginalAsync(callers, state, token);
        await QueueEmptyHistoryPublicAssertions.ChangedAsync(callers, state, token);
        await QueueEmptyHistoryPublicAssertions.ReadyAsync(callers, state, false, token);
        return state;
    }

    private static async Task<bool> FirstColdAsync(RequestCqrsClusterCallers callers,
        QueueEmptyHistoryPublicState state, CancellationToken token)
    {
        await QueueEmptyHistoryPublicAssertions.ReadyAsync(callers, state, false, token);
        await QueueEmptyHistoryPublicAssertions.OriginalAsync(callers, state, token);
        await QueueEmptyHistoryPublicAssertions.ChangedAsync(callers, state, token);
        await QueueEmptyHistoryPublicAssertions.ReadyAsync(callers, state, false, token);
        await SettleAsync(callers, state, QueueEmptyHistoryPublicAssertions.First, 1L, token);
        await QueueEmptyHistoryPublicAssertions.ReadyAsync(callers, state, true, token);
        await QueueEmptyHistoryPublicAssertions.OriginalAsync(callers, state, token);
        await QueueEmptyHistoryPublicAssertions.ChangedAsync(callers, state, token);
        await QueueEmptyHistoryPublicAssertions.ReadyAsync(callers, state, true, token);
        return true;
    }

    private static async Task<bool> SecondColdAsync(RequestCqrsClusterCallers callers,
        QueueEmptyHistoryPublicState state, CancellationToken token)
    {
        await QueueEmptyHistoryPublicAssertions.ReadyAsync(callers, state, true, token);
        await QueueEmptyHistoryPublicAssertions.OriginalAsync(callers, state, token);
        await QueueEmptyHistoryPublicAssertions.ChangedAsync(callers, state, token);
        await QueueEmptyHistoryPublicAssertions.ReadyAsync(callers, state, true, token);
        await SettleAsync(callers, state, QueueEmptyHistoryPublicAssertions.Sibling, 2L, token);
        await QueueEmptyHistoryPublicAssertions.TerminalAsync(callers, state, false, token);
        await QueueLifecyclePublicRoutes.CommitAsync(callers, state.Owner, new(Guid.NewGuid(), state.Owner.Partition,
            [Enqueue(QueueLifecyclePublicProtocol.Healthy)]), token);
        await QueueEmptyHistoryPublicAssertions.MessageAsync(callers, state, QueueLifecyclePublicProtocol.Healthy,
            new(QueueLifecyclePublicProtocol.Healthy, MessageState.Ready, 0, 1L, 3L, null, null), true, token);
        await QueueEmptyHistoryPublicAssertions.OriginalAsync(callers, state, token);
        await QueueEmptyHistoryPublicAssertions.ChangedAsync(callers, state, token);
        await QueueEmptyHistoryPublicAssertions.MessageAsync(callers, state, QueueLifecyclePublicProtocol.Healthy,
            new(QueueLifecyclePublicProtocol.Healthy, MessageState.Ready, 0, 1L, 3L, null, null), true, token);
        await SettleAsync(callers, state, QueueLifecyclePublicProtocol.Healthy, 3L, token);
        await QueueEmptyHistoryPublicAssertions.TerminalAsync(callers, state, true, token);
        await QueueEmptyHistoryPublicAssertions.OriginalAsync(callers, state, token);
        await QueueEmptyHistoryPublicAssertions.ChangedAsync(callers, state, token);
        await QueueEmptyHistoryPublicAssertions.TerminalAsync(callers, state, true, token);
        var fresh = new ReceiveRequest(Guid.NewGuid(), state.Owner.Lane, MaxMessages: 1);
        var empty = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReceiveAsync(fresh, token));
        await Assert.That(empty.Deliveries).IsEmpty();
        await Assert.That(empty.RequestId).IsNotEqualTo(state.EmptyRequest.RequestId);
        var replay = (await McpCallerAssertions.SuccessAsync<ReceiveResult>(
            await callers.Mcp.CallAsync(McpCallerTools.MessagesReceive, fresh, token))).Value;
        await Assert.That(JsonDefaults.Serialize(replay).SequenceEqual(JsonDefaults.Serialize(empty))).IsTrue();
        return true;
    }

    private static async Task SettleAsync(RequestCqrsClusterCallers callers, QueueEmptyHistoryPublicState state,
        string expectedId, long sequence, CancellationToken token)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), state.Owner.Lane, MaxMessages: 1,
            LeaseSeconds: new QueuePolicy().MaxLeaseSeconds);
        var result = await QueueLifecyclePublicRoutes.CallAsync(callers, state.Owner.Route, state.Owner.Partition,
            McpCallerTools.MessagesReceive, request, () => callers.Sdk.ReceiveAsync(request, token), token);
        var delivery = await Assert.That(result.Deliveries).HasSingleItem();
        await Assert.That(result.RequestId).IsEqualTo(request.RequestId);
        await Assert.That(delivery.Id).IsEqualTo(expectedId);
        await Assert.That(delivery.PayloadJson).IsEqualTo(QueueLifecyclePublicProtocol.Payload);
        await Assert.That(delivery.HeadersJson).IsEqualTo(QueueLifecyclePublicProtocol.Headers);
        await Assert.That(delivery.Attempt).IsEqualTo(1);
        await Assert.That(delivery.LeaseVersion).IsEqualTo(1L);
        await Assert.That(delivery.DeliveryGeneration).IsEqualTo(1L);
        var before = await PublicReadAssertions.MessageAsync(await callers.Sdk.InspectAsync(new(state.Owner.Lane, expectedId), token));
        await Assert.That(before!.Metadata.State).IsEqualTo(MessageState.Leased);
        await Assert.That(before.Metadata.StateVersion).IsEqualTo(2L);
        await Assert.That(before.Metadata.ReadySequence).IsEqualTo(sequence);
        await Assert.That(before.Metadata.LeaseUntil).IsEqualTo(delivery.LeaseUntil);
        await QueueEmptyHistoryPublicAssertions.OriginalAsync(callers, state, token);
        await QueueEmptyHistoryPublicAssertions.ChangedAsync(callers, state, token);
        await QueueLeasedCancelPublicAssertions.MessageAsync(callers, state.Owner, expectedId, before, token);
        var replay = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReceiveAsync(request, token));
        await Assert.That(JsonDefaults.Serialize(replay).SequenceEqual(JsonDefaults.Serialize(result))).IsTrue();
        var other = (await McpCallerAssertions.SuccessAsync<ReceiveResult>(
            await callers.Mcp.CallAsync(McpCallerTools.MessagesReceive, request, token))).Value;
        await Assert.That(JsonDefaults.Serialize(other).SequenceEqual(JsonDefaults.Serialize(result))).IsTrue();
        var ack = new DeliveryCommand(Guid.NewGuid(), state.Owner.Lane, delivery.Token, DeliveryAction.Ack);
        var receipt = await QueueLifecyclePublicRoutes.CallAsync(callers, state.Owner.Route, state.Owner.Partition,
            McpCallerTools.MessagesComplete, ack, () => callers.Sdk.CompleteAsync(ack, token), token);
        state.Owner.Deliveries.Add((ack, receipt));
        await Assert.That(receipt.CommandId).IsEqualTo(ack.CommandId);
        await Assert.That(receipt.Mutations).IsEquivalentTo(new MutationReceipt[] {
            new("Ack", state.Owner.Lane.Queue, expectedId, 3L) }, CollectionOrdering.Matching);
        await SagaTimeoutClusterAssertions.AssertSdkErrorAsync(await callers.Sdk.ReceiveAsync(request, token), ErrorCode.StaleLease);
        _ = (await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(
            McpCallerTools.MessagesReceive, request, token), ErrorCode.StaleLease, true)).Value;
    }
}
