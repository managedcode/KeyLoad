using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class SubscriptionPausedDrainFlow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        using var fixture = new TestDatabase();
        fixture.Configure("topic", ResourceKind.Topic);
        var group = SubscriptionTestActions.Configure(fixture, policy: new()
        {
            MaxWindow = 3
        });
        var history = new List<(ReplicatedOperation Operation, OperationResult Result, byte[] Saved)>();
        var time = QueueWholeFlowStorage.Clock(fixture.Store).AddSeconds(1);
        var publish = new CommandRequest(Guid.NewGuid(), group.Source.Partition,
            [new PublishTopic("topic", [new("event1", "Created", "{\"n\":1}"),
                new("event2", "Created", "{\"n\":2}"), new("event3", "Created", "{\"n\":3}")])]);
        Add(fixture.Database, fixture.Store, group, Create(OperationKind.Batch, publish, publish.CommandId, time), history)
            .Get<CommitReceipt>();
        var source = SubscriptionCheckpointAuthorityAssertions.SourceImage(fixture.Store, group.Source.Partition);
        var request = new ReceiveSubscriptionRequest(Guid.NewGuid(), group, MaxEvents: 3);
        var claim = Create(OperationKind.ReceiveSubscription, request, request.RequestId, time.AddSeconds(1));
        var original = fixture.Database.Apply(claim);
        var deliveries = original.Get<ReceiveSubscriptionResult>().Deliveries;
        var claimSaved = SubscriptionProcessingAtomicAssertions.OutcomeBytes(fixture.Store, group, claim.Id);
        await SubscriptionInboxConflictAssertions.InitialClaimsAsync(fixture.Database, group, deliveries, time);
        await NativeReplayResultAssertions.Same<ReceiveSubscriptionResult>(fixture.Database.Apply(claim), original);
        var pause = new SetSubscriptionPausedRequest(Guid.NewGuid(), group, 1, true);
        Add(fixture.Database, fixture.Store, group, Create(OperationKind.SetSubscriptionPaused, pause, pause.CommandId,
            time.AddSeconds(2)), history).Get<SubscriptionInfo>();
        await SubscriptionPausedDrainAssertions.StatusAsync(fixture.Database, group, 0, 3, true);
        var denied = new ReceiveSubscriptionRequest(Guid.NewGuid(), group, MaxEvents: 3);
        await SubscriptionPausedDrainAssertions.RefuseAsync(fixture.Database, fixture.Store, group,
            Create(OperationKind.ReceiveSubscription, denied, denied.RequestId, time.AddSeconds(3)), history);
        var renew = new SubscriptionDeliveryCommand(Guid.NewGuid(), group, deliveries[0].Token, DeliveryAction.Renew, 30);
        var renewed = Add(fixture.Database, fixture.Store, group,
            Create(OperationKind.SubscriptionDelivery, renew, renew.CommandId, time.AddSeconds(4)), history);
        await SubscriptionPausedDrainAssertions.ReceiptAsync(renewed, renew, 1);
        await NativeReplayResultAssertions.Same<ReceiveSubscriptionResult>(fixture.Database.Apply(claim), original);
        await AckAsync(fixture.Database, fixture.Store, group, deliveries[2], history);
        await SubscriptionPausedDrainAssertions.StatusAsync(fixture.Database, group, 0, 3, true);
        await SubscriptionPausedDrainAssertions.OriginalClaimAsync(fixture.Database, fixture.Store, request, original, claimSaved, token);
        await FirstColdAsync(fixture, group, request, original, claimSaved, deliveries, history, source, token);
    }

    private static async Task FirstColdAsync(TestDatabase fixture, SubscriptionRef group, ReceiveSubscriptionRequest request,
        OperationResult original, byte[] claimSaved, global::System.Collections.Immutable.ImmutableArray<SubscriptionDelivery> deliveries,
        List<(ReplicatedOperation Operation, OperationResult Result, byte[] Saved)> history, string[] source, CancellationToken token)
    {
        var cut = QueueWholeFlowStorage.Bytes(fixture.Store);
        var position = fixture.Store.Position;
        var identity = NativeSerialization.Serialize(fixture.Store.Identity);
        fixture.Store.Dispose();
        token.ThrowIfCancellationRequested();
        using var cold = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var db = QueueWholeFlowStorage.Open(cold);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(cold, cut, position, identity);
        await SubscriptionCheckpointAuthorityAssertions.SourceAsync(db, cold, group.Source, source);
        await SubscriptionPausedDrainAssertions.ReplayAsync(db, cold, group, history);
        await SubscriptionPausedDrainAssertions.OriginalClaimAsync(db, cold, request, original, claimSaved, token);
        await SubscriptionPausedDrainAssertions.StatusAsync(db, group, 0, 3, true);
        await AckAsync(db, cold, group, deliveries[0], history);
        await SubscriptionPausedDrainAssertions.StatusAsync(db, group, 1, 3, true);
        await AckAsync(db, cold, group, deliveries[1], history);
        await SubscriptionPausedDrainAssertions.StatusAsync(db, group, 3, 3, true);
        await SubscriptionParkedRecoveryAssertions.NoWindowAsync(cold, group);
        var resume = new SetSubscriptionPausedRequest(Guid.NewGuid(), group, 1, false);
        Add(db, cold, group, Create(OperationKind.SetSubscriptionPaused, resume, resume.CommandId,
            QueueWholeFlowStorage.Clock(cold).AddSeconds(1)), history).Get<SubscriptionInfo>();
        await SubscriptionPausedDrainAssertions.StatusAsync(db, group, 3, 3, false);
        await SubscriptionPausedDrainAssertions.ReplayAsync(db, cold, group, history);
        var fourth = await HealthyAsync(db, cold, group, 4, history, token);
        var after = SubscriptionCheckpointAuthorityAssertions.SourceImage(cold, group.Source.Partition);
        await SecondColdAsync(cold, fixture.Directory, group, request, original, claimSaved, history, after, fourth, token);
    }

    private static async Task SecondColdAsync(ZoneTreeStore store, string directory, SubscriptionRef group,
        ReceiveSubscriptionRequest request, OperationResult original, byte[] claimSaved,
        List<(ReplicatedOperation Operation, OperationResult Result, byte[] Saved)> history, string[] source,
        SourceEventRecord fourth, CancellationToken token)
    {
        var cut = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = NativeSerialization.Serialize(store.Identity);
        store.Dispose();
        token.ThrowIfCancellationRequested();
        using var cold = new ZoneTreeStore(new(directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var db = QueueWholeFlowStorage.Open(cold);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(cold, cut, position, identity);
        var retained = original.Get<ReceiveSubscriptionResult>().Deliveries.Select(delivery => delivery.Event).Append(fourth).ToArray();
        await SubscriptionPausedDrainAssertions.SourceAsync(db, cold, group.Source, source, retained);
        await SubscriptionPausedDrainAssertions.ReplayAsync(db, cold, group, history);
        await SubscriptionPausedDrainAssertions.OriginalClaimAsync(db, cold, request, original, claimSaved, token);
        await SubscriptionPausedDrainAssertions.StatusAsync(db, group, 4, 4, false);
        await SubscriptionParkedRecoveryAssertions.NoWindowAsync(cold, group);
        var fifth = await HealthyAsync(db, cold, group, 5, history, token);
        var after = SubscriptionCheckpointAuthorityAssertions.SourceImage(cold, group.Source.Partition);
        await SubscriptionPausedDrainAssertions.ReplayAsync(db, cold, group, history);
        await SubscriptionPausedDrainAssertions.SourceAsync(db, cold, group.Source, after, [.. retained, fifth]);
        await SubscriptionPausedDrainAssertions.StatusAsync(db, group, 5, 5, false);
    }

    private static async Task<SourceEventRecord> HealthyAsync(DatabaseEngine db, ZoneTreeStore store, SubscriptionRef group, long number,
        List<(ReplicatedOperation Operation, OperationResult Result, byte[] Saved)> history, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var time = QueueWholeFlowStorage.Clock(store).AddSeconds(1);
        var publish = new CommandRequest(Guid.NewGuid(), group.Source.Partition,
            [new PublishTopic("topic", [new("event" + number, "Created", "{\"n\":" + number + "}")])]);
        Add(db, store, group, Create(OperationKind.Batch, publish, publish.CommandId, time), history).Get<CommitReceipt>();
        var request = new ReceiveSubscriptionRequest(Guid.NewGuid(), group, MaxEvents: 1);
        var operation = Create(OperationKind.ReceiveSubscription, request, request.RequestId, time);
        var original = db.Apply(operation);
        var delivery = await Assert.That(original.Get<ReceiveSubscriptionResult>().Deliveries).HasSingleItem();
        var expected = new SubscriptionDelivery(new SourceEventRecord(group.Source, number, number,
            new("event" + number, "Created", "{\"n\":" + number + "}"), time),
            delivery.Token, 1L, time.AddSeconds(30), 1);
        await Assert.That(JsonSerializer.Serialize(delivery, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));
        await NativeReplayResultAssertions.Same<ReceiveSubscriptionResult>(db.Apply(operation), original);
        await AckAsync(db, store, group, delivery, history);
        await SubscriptionCheckpointAuthorityAssertions.SettledClaimAsync(db, store, request, original, token);
        await SubscriptionPausedDrainAssertions.StatusAsync(db, group, number, number, false);
        await SubscriptionParkedRecoveryAssertions.NoWindowAsync(store, group);
        return expected.Event;
    }

    private static async Task AckAsync(DatabaseEngine db, ZoneTreeStore store, SubscriptionRef group, SubscriptionDelivery delivery,
        List<(ReplicatedOperation Operation, OperationResult Result, byte[] Saved)> history)
    {
        var command = new SubscriptionDeliveryCommand(Guid.NewGuid(), group, delivery.Token, DeliveryAction.Ack);
        var result = Add(db, store, group, Create(OperationKind.SubscriptionDelivery, command, command.CommandId,
            QueueWholeFlowStorage.Clock(store).AddSeconds(1)), history);
        await SubscriptionPausedDrainAssertions.ReceiptAsync(result, command, delivery.Event.Position);
    }

    private static OperationResult Add(DatabaseEngine db, ZoneTreeStore store, SubscriptionRef group, ReplicatedOperation operation,
        List<(ReplicatedOperation Operation, OperationResult Result, byte[] Saved)> history)
    {
        var result = db.Apply(operation);
        history.Add((operation, result, SubscriptionProcessingAtomicAssertions.OutcomeBytes(store, group, operation.Id)));
        return result;
    }

    private static ReplicatedOperation Create<T>(OperationKind kind, T payload, Guid id, DateTimeOffset time)
        => new(id, kind, "root", time, JsonSerializer.Serialize(payload, JsonDefaults.Options));
}

