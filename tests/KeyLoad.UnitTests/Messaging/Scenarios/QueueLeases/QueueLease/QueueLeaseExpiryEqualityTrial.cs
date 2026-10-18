using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;
using ManagedCode.Communication;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueLeaseExpiryEqualityTrial
{
    internal static async Task RunAsync()
    {
        using var fixture = new TestDatabase();
        fixture.Configure("jobs", ResourceKind.WorkQueue);
        var state = await SeedExpiredAsync(fixture);
        var identity = NativeSerialization.Serialize(fixture.Store.Identity);
        var firstBytes = QueueWholeFlowStorage.Bytes(fixture.Store);
        var firstPosition = fixture.Store.Position;
        fixture.Store.Dispose();
        using var first = Open(fixture.Directory);
        var database = QueueWholeFlowStorage.Open(first);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(first, firstBytes, firstPosition, identity);
        await ReplayExpiryAsync(database, first, state, firstBytes, firstPosition, identity);
        var (ack, acked) = await ReclaimAsync(database, first, state);
        var secondBytes = QueueWholeFlowStorage.Bytes(first);
        var secondPosition = first.Position;
        first.Dispose();
        using var second = Open(fixture.Directory);
        var restored = QueueWholeFlowStorage.Open(second);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(second, secondBytes, secondPosition, identity);
        await NativeReplayResultAssertions.Same<CommitReceipt>(Apply(restored, ack, state.Boundary), acked);
        await NativeReplayResultAssertions.Same<CommitReceipt>(Batch(restored, state.Producer, state.Boundary), state.Produced);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(second, secondBytes, secondPosition, identity);
        var (producer, produced, healthyAck, healthyReceipt) = await HealthyAsync(restored, state);
        var finalBytes = QueueWholeFlowStorage.Bytes(second);
        var finalPosition = second.Position;
        second.Dispose();
        using var final = Open(fixture.Directory);
        var completed = QueueWholeFlowStorage.Open(final);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(final, finalBytes, finalPosition, identity);
        await NativeReplayResultAssertions.Same<CommitReceipt>(Apply(completed, healthyAck, state.Boundary), healthyReceipt);
        await NativeReplayResultAssertions.Same<CommitReceipt>(Batch(completed, producer, state.Boundary), produced);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(final, finalBytes, finalPosition, identity);
        await Assert.That(completed.InspectMessage("root", state.Lane, "input")!.Metadata.State).IsEqualTo(MessageState.Acked);
        await Assert.That(completed.InspectMessage("root", state.Lane, "healthy")!.Metadata.State).IsEqualTo(MessageState.Acked);
    }

    private static async Task<QueueLeaseExpiryEqualityState> SeedExpiredAsync(TestDatabase fixture)
    {
        var time = QueueWholeFlowStorage.Clock(fixture.Store);
        var lane = new QueueLaneRef(fixture.Partition, "jobs");
        var producer = new CommandRequest(Guid.NewGuid(), fixture.Partition,
            [new EnqueueMessage("jobs", "input", "{\"work\":1}", "{\"kind\":\"job\"}")]);
        var produced = Batch(fixture.Database, producer, time);
        produced.Get<CommitReceipt>();
        var claim = new ReceiveRequest(Guid.NewGuid(), lane, LeaseSeconds: 30);
        var claimed = QueueWholeFlowStorage.Apply(fixture.Database, OperationKind.Receive, claim, claim.RequestId, time);
        var original = await Assert.That(claimed.Get<ReceiveResult>().Deliveries).HasSingleItem();
        await QueueLeaseExpiryEqualityAssertions.DeliveryAsync(original, "input", "{\"work\":1}", "{\"kind\":\"job\"}");
        await Assert.That(original.LeaseUntil).IsEqualTo(time.AddSeconds(30));
        await Assert.That(original.LeaseVersion).IsEqualTo(1L);
        await Assert.That(original.Attempt).IsEqualTo(1);
        var image = QueueOrderedRetryImage.Capture(fixture.Store, lane);
        var denied = new List<(DeliveryCommand Command, Result<object> Result)>();
        foreach (var action in new[] { DeliveryAction.Ack, DeliveryAction.Renew })
        {
            var command = new DeliveryCommand(Guid.NewGuid(), lane, original.Token, action);
            var result = Apply(fixture.Database, command, original.LeaseUntil);
            await QueueLeaseExpiryEqualityAssertions.RefusalAsync(result, ErrorCode.LeaseExpired);
            denied.Add((command, result));
            await QueueOrderedRetryImage.SameAsync(fixture.Store, lane, image);
        }
        return new(lane, original.LeaseUntil, producer, produced, claim, original, denied);
    }

    private static async Task ReplayExpiryAsync(DatabaseEngine database, ZoneTreeStore store,
        QueueLeaseExpiryEqualityState state, string[] image, long position, byte[] identity)
    {
        foreach (var (command, result) in state.Denied)
        {
            var replay = Apply(database, command, state.Boundary);
            await QueueLeaseExpiryEqualityAssertions.RefusalAsync(replay, ErrorCode.LeaseExpired);
            await Assert.That(replay.Problem?.Detail).IsEqualTo(result.Problem?.Detail);
            await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, image, position, identity);
        }
        var claim = QueueWholeFlowStorage.Apply(database, OperationKind.Receive, state.Claim,
            state.Claim.RequestId, state.Boundary);
        await QueueLeaseExpiryEqualityAssertions.RefusalAsync(claim, ErrorCode.LeaseExpired);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, image, position, identity);
    }

    private static async Task<(DeliveryCommand Command, Result<object> Result)> ReclaimAsync(
        DatabaseEngine database, ZoneTreeStore store, QueueLeaseExpiryEqualityState state)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), state.Lane);
        var result = QueueWholeFlowStorage.Apply(database, OperationKind.Receive, request, request.RequestId, state.Boundary);
        var current = await Assert.That(result.Get<ReceiveResult>().Deliveries).HasSingleItem();
        await QueueLeaseExpiryEqualityAssertions.DeliveryAsync(current, state.Original.Id, "{\"work\":1}", "{\"kind\":\"job\"}");
        await Assert.That(current.LeaseVersion).IsEqualTo(2L);
        await Assert.That(current.Attempt).IsEqualTo(2);
        await Assert.That(current.DeliveryGeneration).IsEqualTo(state.Original.DeliveryGeneration);
        await Assert.That(current.Token == state.Original.Token).IsFalse();
        var image = QueueOrderedRetryImage.Capture(store, state.Lane);
        foreach (var action in new[] { DeliveryAction.Ack, DeliveryAction.Renew })
        {
            var command = new DeliveryCommand(Guid.NewGuid(), state.Lane, state.Original.Token, action);
            await QueueLeaseExpiryEqualityAssertions.RefusalAsync(Apply(database, command, state.Boundary), ErrorCode.StaleLease);
            await QueueOrderedRetryImage.SameAsync(store, state.Lane, image);
        }
        var ack = new DeliveryCommand(Guid.NewGuid(), state.Lane, current.Token, DeliveryAction.Ack);
        var acked = Apply(database, ack, state.Boundary);
        acked.Get<CommitReceipt>();
        await Assert.That(database.InspectMessage("root", state.Lane, "input")!.Metadata.State).IsEqualTo(MessageState.Acked);
        return (ack, acked);
    }

    private static async Task<(CommandRequest Producer, Result<object> Produced, DeliveryCommand Ack, Result<object> Receipt)>
        HealthyAsync(DatabaseEngine database, QueueLeaseExpiryEqualityState state)
    {
        var producer = new CommandRequest(Guid.NewGuid(), state.Lane.Partition,
            [new EnqueueMessage("jobs", "healthy", "{\"work\":2}", "{\"kind\":\"healthy\"}")]);
        var produced = Batch(database, producer, state.Boundary);
        produced.Get<CommitReceipt>();
        var request = new ReceiveRequest(Guid.NewGuid(), state.Lane);
        var healthy = await Assert.That(QueueWholeFlowStorage.Apply(database, OperationKind.Receive,
            request, request.RequestId, state.Boundary).Get<ReceiveResult>().Deliveries).HasSingleItem();
        await QueueLeaseExpiryEqualityAssertions.DeliveryAsync(healthy, "healthy", "{\"work\":2}", "{\"kind\":\"healthy\"}");
        await Assert.That(healthy.LeaseVersion).IsEqualTo(1L);
        await Assert.That(healthy.Attempt).IsEqualTo(1);
        var ack = new DeliveryCommand(Guid.NewGuid(), state.Lane, healthy.Token, DeliveryAction.Ack);
        var receipt = Apply(database, ack, state.Boundary);
        receipt.Get<CommitReceipt>();
        return (producer, produced, ack, receipt);
    }

    private static ZoneTreeStore Open(string root) => new(new(root), UnitExecutionOptions.StorageExecution(),
        UnitExecutionOptions.PointCacheExecution());
    private static Result<object> Apply(DatabaseEngine database, DeliveryCommand command, DateTimeOffset time)
        => QueueWholeFlowStorage.Apply(database, OperationKind.Delivery, command, command.CommandId, time);
    private static Result<object> Batch(DatabaseEngine database, CommandRequest command, DateTimeOffset time)
        => QueueWholeFlowStorage.Apply(database, OperationKind.Batch, command, command.CommandId, time);
}
