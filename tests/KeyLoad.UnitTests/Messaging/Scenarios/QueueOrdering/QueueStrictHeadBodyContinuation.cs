using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueStrictHeadBodyContinuation
{
    internal static async Task RunAsync(TestDatabase fixture, QueueStrictHeadBodyState state,
        List<Exception> failures, CancellationToken token)
    {
        fixture.Store.Dispose();
        token.ThrowIfCancellationRequested();
        QueueLifecycleConditionCut? healthy = null;
        using (var cold = new ZoneTreeStore(new(fixture.Directory), fixture.StorageExecution, UnitExecutionOptions.PointCacheExecution()))
        {
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                var owner = fixture.CreateConfiguredDatabase(cold);
                await QueueLifecycleConditionAssertions.Same(cold, state.Damaged);
                await QueueStrictHeadBodyAssertions.RefusalAsync(owner, cold, state);
                QueueLifecycleConditionOperations.Restore(cold, state.BodyKey, state.OriginalBody);
                await QueueLifecycleConditionAssertions.Same(cold, state.Original with
                {
                    Position = state.Original.Position + 2
                });
                await QueueStrictHeadBodyAssertions.ReplayAsync(owner, cold, state.Queue,
                    state.HeadState == MessageState.Leased ? QueueOrderedRetryProtocol.First : null);
                await FinishAsync(owner, cold, state);
                await QueueStrictHeadBodyAssertions.TerminalAsync(owner, cold, state);
                await QueueStrictHeadBodyAssertions.ReplayAsync(owner, cold, state.Queue);
                healthy = QueueLifecycleConditionAssertions.Capture(cold);
            }, failures);
        }
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        var healthyCut = healthy
            ?? throw new InvalidOperationException("The strict-head cold continuation did not complete.");
        token.ThrowIfCancellationRequested();
        using var second = new ZoneTreeStore(new(fixture.Directory), fixture.StorageExecution, UnitExecutionOptions.PointCacheExecution());
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            var owner = fixture.CreateConfiguredDatabase(second);
            await QueueLifecycleConditionAssertions.Same(second, healthyCut);
            await QueueStrictHeadBodyAssertions.TerminalAsync(owner, second, state);
            await QueueStrictHeadBodyAssertions.ReplayAsync(owner, second, state.Queue);
            await QueueLifecycleConditionAssertions.Same(second, healthyCut);
            await QueueStrictHeadBodyAssertions.TerminalAsync(owner, second, state);
        }, failures);
    }

    private static async Task FinishAsync(DatabaseEngine owner, ZoneTreeStore store, QueueStrictHeadBodyState state)
    {
        var queue = state.Queue;
        var received = owner.Apply(state.BlockedReceive);
        var other = await Assert.That(received.Get<ReceiveResult>().Deliveries).HasSingleItem();
        await QueueStrictHeadBodyAssertions.DeliveryAsync(other, QueueOrderedRetryProtocol.Other, queue.Time);
        queue.Outcomes.Add((state.BlockedReceive, received));
        await CurrentAsync(owner, store, queue, state.BlockedReceive, received);
        QueueOrderedRetryOperations.Complete(owner, queue, other, DeliveryAction.Ack).Get<CommitReceipt>();
        await QueueStrictHeadBodyAssertions.HeldAsync(owner, store, state);
        await Assert.That(QueueOrderedRetryOperations.Receive(owner, queue).Deliveries).IsEmpty();
        if (state.HeadState == MessageState.Scheduled)
        {
            queue.Time = state.ScheduledAt;
            queue.FirstLease = await QueueOrderedRetryOperations.ClaimAsync(owner, queue, QueueOrderedRetryProtocol.First);
            await QueueStrictHeadBodyAssertions.DeliveryAsync(queue.FirstLease, QueueOrderedRetryProtocol.First, queue.Time);
            await LastCurrentAsync(owner, store, queue);
        }
        await QueueStrictHeadBodyAssertions.FollowerAsync(owner, store, state);
        await Assert.That(QueueOrderedRetryOperations.Receive(owner, queue).Deliveries).IsEmpty();
        QueueOrderedRetryOperations.Complete(owner, queue, queue.FirstLease!, DeliveryAction.Ack).Get<CommitReceipt>();
        var follower = await QueueOrderedRetryOperations.ClaimAsync(owner, queue, QueueOrderedRetryProtocol.Second);
        await QueueStrictHeadBodyAssertions.DeliveryAsync(follower, QueueOrderedRetryProtocol.Second, queue.Time);
        await LastCurrentAsync(owner, store, queue);
        QueueOrderedRetryOperations.Complete(owner, queue, follower, DeliveryAction.Ack).Get<CommitReceipt>();
        queue.Batch(owner, QueueOrderedRetryOperations.Literal(QueueOrderedRetryProtocol.Healthy)).Get<CommitReceipt>();
        var healthy = await QueueOrderedRetryOperations.ClaimAsync(owner, queue, QueueOrderedRetryProtocol.Healthy);
        await QueueStrictHeadBodyAssertions.DeliveryAsync(healthy, QueueOrderedRetryProtocol.Healthy, queue.Time);
        await LastCurrentAsync(owner, store, queue);
        QueueOrderedRetryOperations.Complete(owner, queue, healthy, DeliveryAction.Ack).Get<CommitReceipt>();
    }
    private static Task LastCurrentAsync(DatabaseEngine owner, ZoneTreeStore store, QueueOrderedRetryState queue)
    {
        var (operation, result) = queue.Outcomes.Last();
        return CurrentAsync(owner, store, queue, operation, result);
    }

    private static async Task CurrentAsync(DatabaseEngine owner, ZoneTreeStore store, QueueOrderedRetryState queue,
        Command<DatabaseCommand> operation, Result<object> result)
    {
        var before = QueueLifecycleConditionAssertions.Capture(store);
        await QueueReceiveReplayAssertions.SameAsync(store, queue.Partition, operation, result, true,
            () => owner.Apply(operation));
        await QueueLifecycleConditionAssertions.Same(store, before);
    }
}
