using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;
using TUnit.Assertions.Enums;

namespace KeyLoad.ClusterTests.Messaging;

internal static class QueueLeasedCancelPublicFlow
{
    internal static async Task RunAsync(ClusterFixture fixture, bool mcpFirst)
    {
        using var deadline = McpCallerDeadline.Create();
        var owner = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            root => QueueLifecyclePublicSeed.RunAsync(root, mcpFirst ? QueueLifecyclePublicProtocol.Mcp : QueueLifecyclePublicProtocol.Sdk,
                deadline.Token), deadline.Token);
        var state = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, owner.OperatorKey,
            callers => StartAsync(fixture, callers, owner, deadline.Token), deadline.Token);
        await ModelSqlClusterCold.RunAsync(fixture, deadline.Token);
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3, owner.OperatorKey,
            callers => ContinueAsync(fixture, callers, state, deadline.Token), deadline.Token);
        await ModelSqlClusterCold.RunAsync(fixture, deadline.Token);
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, owner.OperatorKey,
            callers => FinishAsync(fixture, callers, state, deadline.Token), deadline.Token);
    }

    private static EnqueueMessage Enqueue(string id)
        => new(QueueLifecyclePublicProtocol.Queue, id, QueueLifecyclePublicProtocol.Payload,
            QueueLifecyclePublicProtocol.Headers, OrderingKey: QueueLifecyclePublicProtocol.Ordering);

    private static async Task<QueueLeasedCancelPublicState> StartAsync(ClusterFixture fixture, RequestCqrsClusterCallers callers,
        QueueLifecyclePublicState owner, CancellationToken token)
    {
        await QueueLifecyclePublicRoutes.CommitAsync(callers, owner, new(Guid.NewGuid(), owner.Partition,
            [new PutDocument(QueueLifecyclePublicProtocol.Collection, QueueLeasedCancelPublicAssertions.Witness,
                QueueLifecyclePublicProtocol.Payload)]), token);
        await QueueLifecyclePublicRoutes.CommitAsync(callers, owner, new(Guid.NewGuid(), owner.Partition,
            [Enqueue(QueueLeasedCancelPublicAssertions.Target), Enqueue(QueueLeasedCancelPublicAssertions.Sibling)]), token);
        var request = new ReceiveRequest(Guid.NewGuid(), owner.Lane, MaxMessages: 1, LeaseSeconds: new QueuePolicy().MaxLeaseSeconds);
        var claimed = await QueueLifecyclePublicRoutes.CallAsync(callers, owner.Route, owner.Partition,
            McpCallerTools.MessagesReceive, request, () => callers.Sdk.ReceiveAsync(request, token), token);
        var delivery = await Assert.That(claimed.Deliveries).HasSingleItem();
        await Assert.That(delivery.Id).IsEqualTo(QueueLeasedCancelPublicAssertions.Target);
        await Assert.That(delivery.Attempt).IsEqualTo(1);
        await Assert.That(delivery.LeaseVersion).IsEqualTo(1L);
        await Assert.That(delivery.PayloadJson).IsEqualTo(QueueLifecyclePublicProtocol.Payload);
        await Assert.That(delivery.HeadersJson).IsEqualTo(QueueLifecyclePublicProtocol.Headers);
        var leased = await PublicReadAssertions.MessageAsync(await callers.Sdk.InspectAsync(
            new(owner.Lane, QueueLeasedCancelPublicAssertions.Target), token));
        await Assert.That(leased!.Metadata.State).IsEqualTo(MessageState.Leased);
        await Assert.That(leased.Metadata.StateVersion).IsEqualTo(2L);
        await Assert.That(leased.Metadata.LeaseUntil).IsEqualTo(delivery.LeaseUntil);
        await Assert.That(leased.Metadata.LeaseOwner).IsNotNull();
        var expectedLeased = new MessageInspection(new MessageMetadata(QueueLeasedCancelPublicAssertions.Target,
            MessageState.Leased, 1, 2L, 1L, null, null, leased.Metadata.LeaseOwner, 1L, delivery.LeaseUntil),
            QueueLifecyclePublicProtocol.Payload, QueueLifecyclePublicProtocol.Headers);
        await Assert.That(JsonDefaults.Serialize(leased).SequenceEqual(JsonDefaults.Serialize(expectedLeased))).IsTrue();
        var state = new QueueLeasedCancelPublicState(owner, request, claimed, leased);
        await DeniedAsync(fixture, state, token);
        await QueueLeasedCancelPublicAssertions.ImagesAsync(callers, state, false, token);
        var mixed = new CommandRequest(Guid.NewGuid(), owner.Partition,
            [new CancelQueueMessage(owner.Lane.Queue, QueueLeasedCancelPublicAssertions.Target, 2L, 1L),
             new PutDocument(QueueLifecyclePublicProtocol.Collection, QueueLeasedCancelPublicAssertions.Witness,
                 "{\"knowledge\":\"must not replace\"}", 0)]);
        state.Failures.Add((mixed, ErrorCode.RevisionConflict, await QueueLeasedCancelPublicAssertions.RefusedAsync(
            callers, owner, mixed, ErrorCode.RevisionConflict, token)));
        await QueueLeasedCancelPublicAssertions.ImagesAsync(callers, state, false, token);
        await Assert.That(JsonDefaults.Serialize(await McpCallerAssertions.SdkSuccessAsync(
            await callers.Sdk.ReceiveAsync(request, token))).SequenceEqual(JsonDefaults.Serialize(claimed))).IsTrue();
        await Assert.That(JsonDefaults.Serialize((await McpCallerAssertions.SuccessAsync<ReceiveResult>(
            await callers.Mcp.CallAsync(McpCallerTools.MessagesReceive, request, token))).Value)
            .SequenceEqual(JsonDefaults.Serialize(claimed))).IsTrue();
        var cancel = new CommandRequest(Guid.NewGuid(), owner.Partition,
            [new CancelQueueMessage(owner.Lane.Queue, QueueLeasedCancelPublicAssertions.Target, 2L, 1L)]);
        var receipt = await QueueLifecyclePublicRoutes.CommitAsync(callers, owner, cancel, token);
        await Assert.That(receipt.CommandId).IsEqualTo(cancel.CommandId);
        await Assert.That(receipt.Mutations).IsEquivalentTo(new MutationReceipt[] {
            new("cancelQueueMessage", owner.Lane.Queue, QueueLeasedCancelPublicAssertions.Target, 3L) }, CollectionOrdering.Matching);
        await QueueLeasedCancelPublicAssertions.ImagesAsync(callers, state, true, token);
        await QueueLeasedCancelPublicAssertions.OriginalAsync(callers, state, token);
        await QueueLeasedCancelPublicAssertions.ImagesAsync(callers, state, true, token);
        return state;
    }

    private static async Task<bool> ContinueAsync(ClusterFixture fixture, RequestCqrsClusterCallers callers,
        QueueLeasedCancelPublicState state, CancellationToken token)
    {
        var owner = state.Owner;
        await DeniedAsync(fixture, state, token);
        await QueueLeasedCancelPublicAssertions.ImagesAsync(callers, state, true, token);
        await QueueLeasedCancelPublicAssertions.OriginalAsync(callers, state, token);
        var terminal = new CommandRequest(Guid.NewGuid(), owner.Partition,
            [new CancelQueueMessage(owner.Lane.Queue, QueueLeasedCancelPublicAssertions.Target, 3L, 2L)]);
        state.Failures.Add((terminal, ErrorCode.Conflict, await QueueLeasedCancelPublicAssertions.RefusedAsync(
            callers, owner, terminal, ErrorCode.Conflict, token)));
        var resurrect = new CommandRequest(Guid.NewGuid(), owner.Partition, [Enqueue(QueueLeasedCancelPublicAssertions.Target)]);
        state.Failures.Add((resurrect, ErrorCode.Conflict, await QueueLeasedCancelPublicAssertions.RefusedAsync(
            callers, owner, resurrect, ErrorCode.Conflict, token)));
        await QueueLeasedCancelPublicAssertions.ImagesAsync(callers, state, true, token);
        await QueueLifecyclePublicRoutes.CommitAsync(callers, owner, new(Guid.NewGuid(), owner.Partition,
            [Enqueue(QueueLeasedCancelPublicAssertions.Replacement)]), token);
        await SettleAsync(callers, state, [QueueLeasedCancelPublicAssertions.Sibling, QueueLeasedCancelPublicAssertions.Replacement], token);
        await QueueLeasedCancelPublicAssertions.OriginalAsync(callers, state, token);
        await QueueLeasedCancelPublicAssertions.FinalImagesAsync(callers, state, healthy: false, token);
        return true;
    }

    private static async Task SettleAsync(RequestCqrsClusterCallers callers, QueueLeasedCancelPublicState state,
        string[] ids, CancellationToken token)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), state.Owner.Lane, MaxMessages: ids.Length,
            LeaseSeconds: new QueuePolicy().MaxLeaseSeconds);
        var result = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReceiveAsync(request, token));
        await Assert.That(result.Deliveries.Select(d => d.Id)).IsEquivalentTo(ids, CollectionOrdering.Matching);
        foreach (var delivery in result.Deliveries)
        {
            await Assert.That(delivery.PayloadJson).IsEqualTo(QueueLifecyclePublicProtocol.Payload);
            await Assert.That(delivery.HeadersJson).IsEqualTo(QueueLifecyclePublicProtocol.Headers);
            var ack = new DeliveryCommand(Guid.NewGuid(), state.Owner.Lane, delivery.Token, DeliveryAction.Ack);
            var receipt = await QueueLifecyclePublicRoutes.CallAsync(callers, state.Owner.Route, state.Owner.Partition,
                McpCallerTools.MessagesComplete, ack, () => callers.Sdk.CompleteAsync(ack, token), token);
            state.Owner.Deliveries.Add((ack, receipt));
            await Assert.That(receipt.CommandId).IsEqualTo(ack.CommandId);
            await Assert.That(receipt.Mutations).IsEquivalentTo(new MutationReceipt[] {
                new("Ack", state.Owner.Lane.Queue, delivery.Id, 3L) }, CollectionOrdering.Matching);
        }
    }

    private static async Task<bool> FinishAsync(ClusterFixture fixture, RequestCqrsClusterCallers callers,
        QueueLeasedCancelPublicState state, CancellationToken token)
    {
        await DeniedAsync(fixture, state, token);
        await QueueLeasedCancelPublicAssertions.OriginalAsync(callers, state, token);
        await QueueLeasedCancelPublicAssertions.MessageAsync(callers, state.Owner, QueueLeasedCancelPublicAssertions.Target,
            new(new(QueueLeasedCancelPublicAssertions.Target, MessageState.Cancelled, 1, 3L, 1L, null, null,
                LeaseVersion: 2L, DeliveryGeneration: 2L), null, null), token);
        await QueueLifecyclePublicRoutes.CommitAsync(callers, state.Owner, new(Guid.NewGuid(), state.Owner.Partition,
            [Enqueue(QueueLifecyclePublicProtocol.Healthy)]), token);
        await SettleAsync(callers, state, [QueueLifecyclePublicProtocol.Healthy], token);
        await QueueLeasedCancelPublicAssertions.OriginalAsync(callers, state, token);
        await QueueLeasedCancelPublicAssertions.FinalImagesAsync(callers, state, healthy: true, token);
        return true;
    }

    private static Task<bool> DeniedAsync(ClusterFixture fixture, QueueLeasedCancelPublicState state, CancellationToken token)
        => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Owner.DeniedKey, async callers =>
    {
        state.Denied ??= new(Guid.NewGuid(), state.Owner.Partition,
            [new CancelQueueMessage(state.Owner.Lane.Queue, QueueLeasedCancelPublicAssertions.Target, 2L, 1L)]);
        var problem = await QueueLeasedCancelPublicAssertions.RefusedAsync(callers, state.Owner,
            state.Denied, ErrorCode.PermissionDenied, token);
        if (state.DeniedProblem is { } original)
        {
            await Assert.That(JsonDefaults.Serialize(problem).SequenceEqual(JsonDefaults.Serialize(original))).IsTrue();
        }
        else
        {
            state.DeniedProblem = problem;
        }
        return true;
    }, token);

}
