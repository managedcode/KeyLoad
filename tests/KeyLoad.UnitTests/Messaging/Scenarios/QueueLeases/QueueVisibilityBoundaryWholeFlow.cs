using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueVisibilityBoundaryWholeFlow
{
    internal const string Queue = "jobs";
    internal const string Message = "visibility-boundary";
    internal const string Healthy = "visibility-healthy";

    internal static async Task RunAsync(TestDatabase fixture, DateTimeOffset start, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var lane = new QueueLaneRef(fixture.Partition, Queue);
        var outcomes = new List<(Command<DatabaseCommand> Operation, Result<object> Result)>();
        var originalRow = fixture.Store.Read(view => view.ReadOwnedValue(KeySpace.Partition("message-meta",
            fixture.Partition, Queue, "input"))) ?? throw new InvalidOperationException("The original terminal input is absent.");
        var (original, originalBody) = await BeginAsync(fixture.Database, fixture.Store, lane, start, outcomes);
        var boundary = original.LeaseUntil.AddSeconds(1);
        var cut = QueueVisibilityBoundaryAssertions.Capture(fixture.Store);
        fixture.Store.Dispose();
        var failures = new List<Exception>();
        try
        {
            using var cold = new ZoneTreeStore(new(fixture.Directory), fixture.StorageExecution, UnitExecutionOptions.PointCacheExecution());
            try
            {
                var owner = fixture.CreateConfiguredDatabase(cold);
                await QueueVisibilityBoundaryAssertions.SameAsync(cold, cut);
                await QueueVisibilityBoundaryAssertions.ReplayAsync(owner, cold, outcomes, original.Token);
                await QueueVisibilityBoundaryAssertions.RefuseAsync(owner, cold, lane, original,
                    boundary, ErrorCode.LeaseExpired, "The delivery lease has expired.", outcomes, original.Token);
                await FinishAsync(owner, cold, lane, original, boundary, outcomes, originalRow, originalBody);
                cut = QueueVisibilityBoundaryAssertions.Capture(cold);
            }
            catch (Exception error) when (global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            catch (Exception error) when (!global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        }
        catch (Exception error) when (global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
        token.ThrowIfCancellationRequested();
        await SecondColdAsync(fixture, lane, outcomes, cut, originalRow);
    }

    private static async Task<(Delivery Delivery, byte[] Body)> BeginAsync(DatabaseEngine owner, ZoneTreeStore store, QueueLaneRef lane,
        DateTimeOffset start, List<(Command<DatabaseCommand> Operation, Result<object> Result)> outcomes)
    {
        Batch(owner, lane, start, outcomes, new EnqueueMessage(Queue, Message, "{}")).Get<CommitReceipt>();
        var originalBody = store.Read(view => view.ReadOwnedValue(KeySpace.Partition("message-body", lane.Partition, Queue, Message)))
            ?? throw new InvalidOperationException("The genuine producer-persisted boundary body is absent.");
        await QueueVisibilityBoundaryAssertions.MessageAsync(owner, lane,
            new(Message, MessageState.Ready, 0, 1, 3, null, null), stored: true, originalBody: originalBody);
        var original = await ReceiveAsync(owner, store, lane, start, outcomes, Message, seconds: 1);
        await QueueVisibilityBoundaryAssertions.MessageAsync(owner, lane,
            new(Message, MessageState.Leased, 1, 2, 3, null, null, LeaseOwner: "root",
                LeaseVersion: 1, LeaseUntil: original.LeaseUntil), stored: true, originalBody: originalBody);
        var before = QueueLifecycleImage.Capture(store, lane);
        await Assert.That(Receive(owner, lane, original.LeaseUntil.AddTicks(-1), outcomes, seconds: 1).Deliveries).IsEmpty();
        await QueueLifecycleImage.SameAsync(store, lane, before);
        Complete(owner, lane, original, original.LeaseUntil.AddTicks(-1), outcomes, DeliveryAction.Renew, 1).Get<CommitReceipt>();
        await QueueVisibilityBoundaryAssertions.DeliveryReceiptAsync(outcomes[^1], DeliveryAction.Renew, Message, 3);
        await QueueVisibilityBoundaryAssertions.MessageAsync(owner, lane,
            new(Message, MessageState.Leased, 1, 3, 3, null, null, LeaseOwner: "root",
                LeaseVersion: 1, LeaseUntil: original.LeaseUntil.AddSeconds(1).AddTicks(-1)), stored: true, originalBody: originalBody);
        Complete(owner, lane, original, original.LeaseUntil, outcomes, DeliveryAction.Renew, 1).Get<CommitReceipt>();
        await QueueVisibilityBoundaryAssertions.DeliveryReceiptAsync(outcomes[^1], DeliveryAction.Renew, Message, 4);
        await QueueVisibilityBoundaryAssertions.MessageAsync(owner, lane,
            new(Message, MessageState.Leased, 1, 4, 3, null, null, LeaseOwner: "root",
                LeaseVersion: 1, LeaseUntil: original.LeaseUntil.AddSeconds(1)), stored: true, originalBody: originalBody);
        await QueueVisibilityBoundaryAssertions.RefuseAsync(owner, store, lane, original,
            original.LeaseUntil.AddSeconds(1), ErrorCode.LeaseExpired, "The delivery lease has expired.", outcomes, original.Token);
        await QueueVisibilityBoundaryAssertions.ReplayAsync(owner, store, outcomes, original.Token);
        return (original, originalBody);
    }

    private static async Task FinishAsync(DatabaseEngine owner, ZoneTreeStore store, QueueLaneRef lane,
        Delivery original, DateTimeOffset boundary, List<(Command<DatabaseCommand> Operation, Result<object> Result)> outcomes,
        byte[] originalRow, byte[] originalBody)
    {
        var current = await ReceiveAsync(owner, store, lane, boundary, outcomes, Message, seconds: 10, attempt: 2, version: 2);
        await Assert.That(current.LeaseVersion).IsEqualTo(2L);
        await Assert.That(current.Token).IsNotEqualTo(original.Token);
        await QueueVisibilityBoundaryAssertions.MessageAsync(owner, lane,
            new(Message, MessageState.Leased, 2, 6, 4, null, null, LeaseOwner: "root",
                LeaseVersion: 2, LeaseUntil: boundary.AddSeconds(10)), stored: true, originalBody: originalBody);
        await QueueVisibilityBoundaryAssertions.RefuseAsync(owner, store, lane, original,
            boundary, ErrorCode.StaleLease, "The delivery lease is stale.", outcomes, current.Token);
        Complete(owner, lane, current, boundary, outcomes, DeliveryAction.Ack).Get<CommitReceipt>();
        await QueueVisibilityBoundaryAssertions.DeliveryReceiptAsync(outcomes[^1], DeliveryAction.Ack, Message, 7);
        Batch(owner, lane, boundary, outcomes, new EnqueueMessage(Queue, Healthy, "{}")).Get<CommitReceipt>();
        var healthy = await ReceiveAsync(owner, store, lane, boundary, outcomes, Healthy, seconds: 10);
        Complete(owner, lane, healthy, boundary, outcomes, DeliveryAction.Ack).Get<CommitReceipt>();
        await QueueVisibilityBoundaryAssertions.DeliveryReceiptAsync(outcomes[^1], DeliveryAction.Ack, Healthy, 3);
        await QueueVisibilityBoundaryAssertions.TerminalAsync(owner, store, lane, originalRow);
        await QueueVisibilityBoundaryAssertions.ReplayAsync(owner, store, outcomes);
        await QueueVisibilityBoundaryAssertions.TerminalAsync(owner, store, lane, originalRow);
    }

    private static async Task SecondColdAsync(TestDatabase fixture, QueueLaneRef lane,
        List<(Command<DatabaseCommand> Operation, Result<object> Result)> outcomes, (long Position, byte[] Identity, string[] Bytes) cut,
        byte[] originalRow)
    {
        var failures = new List<Exception>();
        try
        {
            using var cold = new ZoneTreeStore(new(fixture.Directory), fixture.StorageExecution, UnitExecutionOptions.PointCacheExecution());
            try
            {
                var owner = fixture.CreateConfiguredDatabase(cold);
                await QueueVisibilityBoundaryAssertions.SameAsync(cold, cut);
                await QueueVisibilityBoundaryAssertions.TerminalAsync(owner, cold, lane, originalRow);
                await QueueVisibilityBoundaryAssertions.ReplayAsync(owner, cold, outcomes);
                await QueueVisibilityBoundaryAssertions.SameAsync(cold, cut);
                await QueueVisibilityBoundaryAssertions.TerminalAsync(owner, cold, lane, originalRow);
            }
            catch (Exception error) when (global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            catch (Exception error) when (!global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        }
        catch (Exception error) when (global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static Result<object> Submit<T>(DatabaseEngine owner, OperationKind kind, T payload,
        Guid id, DateTimeOffset time, List<(Command<DatabaseCommand> Operation, Result<object> Result)> outcomes)
    {
        var operation = owner.CreateCommand(kind, id, "root", time, JsonSerializer.Serialize(payload, JsonDefaults.Options));
        var result = owner.Apply(operation);
        outcomes.Add((operation, result));
        return result;
    }

    private static Result<object> Batch(DatabaseEngine owner, QueueLaneRef lane, DateTimeOffset time,
        List<(Command<DatabaseCommand> Operation, Result<object> Result)> outcomes, Mutation mutation)
    {
        var id = Guid.NewGuid();
        return Submit(owner, OperationKind.Batch, new CommandRequest(id, lane.Partition, [mutation]), id, time, outcomes);
    }

    private static ReceiveResult Receive(DatabaseEngine owner, QueueLaneRef lane, DateTimeOffset time,
        List<(Command<DatabaseCommand> Operation, Result<object> Result)> outcomes, int seconds)
    {
        var id = Guid.NewGuid();
        return Submit(owner, OperationKind.Receive, new ReceiveRequest(id, lane, LeaseSeconds: seconds), id, time, outcomes).Get<ReceiveResult>();
    }

    private static async Task<Delivery> ReceiveAsync(DatabaseEngine owner, ZoneTreeStore store, QueueLaneRef lane, DateTimeOffset time,
        List<(Command<DatabaseCommand> Operation, Result<object> Result)> outcomes, string id, int seconds,
        int attempt = 1, int version = 1)
    {
        var delivery = await Assert.That(Receive(owner, lane, time, outcomes, seconds).Deliveries).HasSingleItem();
        await Assert.That(delivery.Id).IsEqualTo(id);
        await Assert.That(delivery.PayloadJson).IsEqualTo("{}");
        await Assert.That(delivery.HeadersJson).IsEqualTo("{}");
        await Assert.That(delivery.Token).IsNotEmpty();
        await Assert.That(delivery).IsEqualTo(new Delivery(id, "{}", "{}", delivery.Token,
            LeaseVersion: version, LeaseUntil: time.AddSeconds(seconds), Attempt: attempt, DeliveryGeneration: 1L));
        var original = outcomes.Last();
        var cut = QueueVisibilityBoundaryAssertions.Capture(store);
        await QueueReceiveReplayAssertions.SameAsync(store, lane.Partition, original.Operation,
            original.Result, true, () => owner.Apply(original.Operation));
        await QueueVisibilityBoundaryAssertions.SameAsync(store, cut);
        return delivery;
    }

    private static Result<object> Complete(DatabaseEngine owner, QueueLaneRef lane, Delivery delivery, DateTimeOffset time,
        List<(Command<DatabaseCommand> Operation, Result<object> Result)> outcomes, DeliveryAction action, int? seconds = null)
    {
        var id = Guid.NewGuid();
        return Submit(owner, OperationKind.Delivery, new DeliveryCommand(id, lane, delivery.Token, action, seconds),
            id, time, outcomes);
    }
}
