using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using TUnit.Assertions.Enums;
namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueScheduledCancelPublicFlow
{
    private const int FutureHours = 1;
    internal static async Task RunAsync(ClusterFixture fixture, bool mcpFirst)
    {
        using var deadline = McpCallerDeadline.Create();
        var owner = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            root => QueueLifecyclePublicSeed.RunAsync(root, mcpFirst ? QueueLifecyclePublicProtocol.Mcp : QueueLifecyclePublicProtocol.Sdk,
                deadline.Token), deadline.Token);
        var state = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, owner.OperatorKey,
            callers => StartAsync(fixture, callers, owner, deadline.Token), deadline.Token);
        await ModelSqlRf3Cold.RunAsync(fixture, deadline.Token);
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3, owner.OperatorKey,
            callers => FirstColdAsync(fixture, callers, state, deadline.Token), deadline.Token);
        await ModelSqlRf3Cold.RunAsync(fixture, deadline.Token);
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, owner.OperatorKey,
            callers => SecondColdAsync(fixture, callers, state, deadline.Token), deadline.Token);
    }

    private static EnqueueMessage Enqueue(string id, DateTimeOffset? due = null)
        => new(QueueLifecyclePublicProtocol.Queue, id, QueueLifecyclePublicProtocol.Payload,
            QueueLifecyclePublicProtocol.Headers, NotBefore: due, OrderingKey: QueueLifecyclePublicProtocol.Ordering);

    private static CommandRequest Hint(QueueScheduledCancelPublicState state)
        => new(Guid.NewGuid(), state.Owner.Partition,
            [new AdvanceQueueDeadline(state.Owner.Lane.Queue, QueueScheduledCancelPublicAssertions.Target,
                MessageState.Scheduled, 1L, 0L, state.Due, QueueDeadlineKind.PromoteScheduled)]);

    private static async Task<QueueScheduledCancelPublicState> StartAsync(ClusterFixture fixture,
        RequestCqrsRf3Callers callers, QueueLifecyclePublicState owner, CancellationToken token)
    {
        var state = new QueueScheduledCancelPublicState(owner, TimeProvider.System.GetUtcNow().AddHours(FutureHours));
        await QueueLifecyclePublicRoutes.CommitAsync(callers, owner, new(Guid.NewGuid(), owner.Partition,
            [new PutDocument(QueueLifecyclePublicProtocol.Collection, QueueScheduledCancelPublicAssertions.Witness,
                QueueLifecyclePublicProtocol.Payload),
             Enqueue(QueueScheduledCancelPublicAssertions.Target, state.Due),
             Enqueue(QueueScheduledCancelPublicAssertions.Sibling)]), token);
        await QueueScheduledCancelPublicAssertions.CutAsync(callers, state, false, token);
        await DeniedAsync(fixture, state, token);
        await QueueScheduledCancelPublicAssertions.FailureAsync(callers, state, Hint(state), ErrorCode.Conflict, token);
        var mixed = new CommandRequest(Guid.NewGuid(), owner.Partition,
            [new CancelQueueMessage(owner.Lane.Queue, QueueScheduledCancelPublicAssertions.Target, 1L, 1L),
             new PutDocument(QueueLifecyclePublicProtocol.Collection, QueueScheduledCancelPublicAssertions.Witness,
                "{\"knowledge\":\"must not replace\"}", 0L)]);
        await QueueScheduledCancelPublicAssertions.FailureAsync(callers, state, mixed, ErrorCode.RevisionConflict, token);
        await QueueScheduledCancelPublicAssertions.CutAsync(callers, state, false, token);
        var cancel = new CommandRequest(Guid.NewGuid(), owner.Partition,
            [new CancelQueueMessage(owner.Lane.Queue, QueueScheduledCancelPublicAssertions.Target, 1L, 1L)]);
        var receipt = await QueueLifecyclePublicRoutes.CommitAsync(callers, owner, cancel, token);
        await Assert.That(receipt.Mutations).IsEquivalentTo(new MutationReceipt[] {
            new("cancelQueueMessage", owner.Lane.Queue, QueueScheduledCancelPublicAssertions.Target, 2L) }, CollectionOrdering.Matching);
        await QueueScheduledCancelPublicAssertions.CutAsync(callers, state, true, token);
        await QueueScheduledCancelPublicAssertions.FailureAsync(callers, state, Hint(state), ErrorCode.RevisionConflict, token);
        await QueueScheduledCancelPublicAssertions.OriginalsAsync(callers, state, token);
        await QueueScheduledCancelPublicAssertions.CutAsync(callers, state, true, token);
        return state;
    }

    private static async Task<bool> FirstColdAsync(ClusterFixture fixture, RequestCqrsRf3Callers callers,
        QueueScheduledCancelPublicState state, CancellationToken token)
    {
        await DeniedAsync(fixture, state, token);
        await QueueScheduledCancelPublicAssertions.OriginalsAsync(callers, state, token);
        await QueueScheduledCancelPublicAssertions.CutAsync(callers, state, true, token);
        await QueueScheduledCancelPublicAssertions.FailureAsync(callers, state, Hint(state), ErrorCode.RevisionConflict, token);
        var terminal = new CommandRequest(Guid.NewGuid(), state.Owner.Partition,
            [new CancelQueueMessage(state.Owner.Lane.Queue, QueueScheduledCancelPublicAssertions.Target, 2L, 2L)]);
        await QueueScheduledCancelPublicAssertions.FailureAsync(callers, state, terminal, ErrorCode.Conflict, token);
        await QueueScheduledCancelPublicAssertions.FailureAsync(callers, state, new(Guid.NewGuid(), state.Owner.Partition,
            [Enqueue(QueueScheduledCancelPublicAssertions.Target, state.Due)]), ErrorCode.Conflict, token);
        await QueueScheduledCancelPublicAssertions.CutAsync(callers, state, true, token);
        await QueueLifecyclePublicRoutes.CommitAsync(callers, state.Owner, new(Guid.NewGuid(), state.Owner.Partition,
            [Enqueue(QueueScheduledCancelPublicAssertions.Replacement)]), token);
        await SettleAsync(callers, state,
            [QueueScheduledCancelPublicAssertions.Sibling, QueueScheduledCancelPublicAssertions.Replacement], token);
        await FinalAsync(callers, state, false, token);
        return true;
    }

    private static async Task<bool> SecondColdAsync(ClusterFixture fixture, RequestCqrsRf3Callers callers,
        QueueScheduledCancelPublicState state, CancellationToken token)
    {
        await DeniedAsync(fixture, state, token);
        await FinalAsync(callers, state, false, token);
        await QueueScheduledCancelPublicAssertions.FailureAsync(callers, state, Hint(state), ErrorCode.RevisionConflict, token);
        await QueueLifecyclePublicRoutes.CommitAsync(callers, state.Owner, new(Guid.NewGuid(), state.Owner.Partition,
            [Enqueue(QueueLifecyclePublicProtocol.Healthy)]), token);
        await SettleAsync(callers, state, [QueueLifecyclePublicProtocol.Healthy], token);
        await FinalAsync(callers, state, true, token);
        var empty = new ReceiveRequest(Guid.NewGuid(), state.Owner.Lane);
        var result = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReceiveAsync(empty, token));
        await Assert.That(result.Deliveries).IsEmpty();
        var replay = (await McpCallerAssertions.SuccessAsync<ReceiveResult>(
            await callers.Mcp.CallAsync(McpCallerTools.MessagesReceive, empty, token))).Value;
        await Assert.That(JsonDefaults.Serialize(replay).SequenceEqual(JsonDefaults.Serialize(result))).IsTrue();
        return true;
    }

    private static async Task SettleAsync(RequestCqrsRf3Callers callers, QueueScheduledCancelPublicState state,
        string[] ids, CancellationToken token)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), state.Owner.Lane, MaxMessages: ids.Length,
            LeaseSeconds: new QueuePolicy().MaxLeaseSeconds);
        var result = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReceiveAsync(request, token));
        await Assert.That(result.Deliveries.Select(d => d.Id)).IsEquivalentTo(ids, CollectionOrdering.Matching);
        var replay = (await McpCallerAssertions.SuccessAsync<ReceiveResult>(
            await callers.Mcp.CallAsync(McpCallerTools.MessagesReceive, request, token))).Value;
        await Assert.That(JsonDefaults.Serialize(replay).SequenceEqual(JsonDefaults.Serialize(result))).IsTrue();
        foreach (var delivery in result.Deliveries)
        {
            await Assert.That(delivery.PayloadJson).IsEqualTo(QueueLifecyclePublicProtocol.CanonicalPayload);
            await Assert.That(delivery.HeadersJson).IsEqualTo(QueueLifecyclePublicProtocol.Headers);
            await Assert.That(delivery.Attempt).IsEqualTo(1);
            await Assert.That(delivery.LeaseVersion).IsEqualTo(1L);
            await Assert.That(delivery.DeliveryGeneration).IsEqualTo(1L);
            var ack = new DeliveryCommand(Guid.NewGuid(), state.Owner.Lane, delivery.Token, DeliveryAction.Ack);
            var receipt = await QueueLifecyclePublicRoutes.CallAsync(callers, state.Owner.Route, state.Owner.Partition,
                McpCallerTools.MessagesComplete, ack, () => callers.Sdk.CompleteAsync(ack, token), token);
            state.Owner.Deliveries.Add((ack, receipt));
            await Assert.That(receipt.Mutations).IsEquivalentTo(new MutationReceipt[] {
                new("Ack", state.Owner.Lane.Queue, delivery.Id, 3L) }, CollectionOrdering.Matching);
        }
        await SagaTimeoutRf3Assertions.AssertSdkErrorAsync(await callers.Sdk.ReceiveAsync(request, token), ErrorCode.StaleLease);
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(McpCallerTools.MessagesReceive, request, token),
            ErrorCode.StaleLease, true);
    }

    private static async Task FinalAsync(RequestCqrsRf3Callers callers, QueueScheduledCancelPublicState state,
        bool healthy, CancellationToken token)
    {
        await QueueScheduledCancelPublicAssertions.MessageAsync(callers, state, QueueScheduledCancelPublicAssertions.Target,
            new(QueueScheduledCancelPublicAssertions.Target, MessageState.Cancelled, 0, 2L, 0L, null, null,
                LeaseVersion: 1L, DeliveryGeneration: 2L), false, token);
        await QueueScheduledCancelPublicAssertions.TerminalAsync(callers, state, QueueScheduledCancelPublicAssertions.Sibling, 1L, token);
        await QueueScheduledCancelPublicAssertions.TerminalAsync(callers, state, QueueScheduledCancelPublicAssertions.Replacement, 2L, token);
        if (healthy)
        { await QueueScheduledCancelPublicAssertions.TerminalAsync(callers, state, QueueLifecyclePublicProtocol.Healthy, 3L, token); }
        var witness = await PublicReadAssertions.DocumentAsync(await callers.Sdk.GetAsync(
            new(state.Owner.Partition, QueueLifecyclePublicProtocol.Collection, QueueScheduledCancelPublicAssertions.Witness), token));
        await Assert.That(witness!.Revision).IsEqualTo(1L);
        await Assert.That(witness.Json).IsEqualTo(QueueLifecyclePublicProtocol.Payload);
        await QueueScheduledCancelPublicAssertions.OriginalsAsync(callers, state, token);
    }

    private static Task<bool> DeniedAsync(ClusterFixture fixture, QueueScheduledCancelPublicState state, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Owner.DeniedKey, async callers =>
    {
        state.Denied ??= new(Guid.NewGuid(), state.Owner.Partition,
            [new CancelQueueMessage(state.Owner.Lane.Queue, QueueScheduledCancelPublicAssertions.Target, 1L, 1L)]);
        var problem = await QueueLeasedCancelPublicAssertions.RefusedAsync(
            callers, state.Owner, state.Denied, ErrorCode.PermissionDenied, token);
        if (state.DeniedProblem is { } saved)
        { await Assert.That(JsonDefaults.Serialize(problem).SequenceEqual(JsonDefaults.Serialize(saved))).IsTrue(); }
        else
        { state.DeniedProblem = problem; }
        return true;
    }, token);
}
