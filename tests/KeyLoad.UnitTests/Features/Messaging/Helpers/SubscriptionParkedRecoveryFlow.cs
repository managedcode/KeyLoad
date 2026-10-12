using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class SubscriptionParkedRecoveryFlow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        using var fixture = new TestDatabase();
        fixture.Configure("topic", ResourceKind.Topic);
        SubscriptionTestActions.Publish(fixture);
        var policy = new SubscriptionPolicy { MaxWindow = 3, MaxAttempts = 2, MaxLeaseSeconds = 1 };
        var group = SubscriptionTestActions.Configure(fixture, policy: policy);
        var source = SubscriptionCheckpointAuthorityAssertions.SourceImage(fixture.Store, group.Source.Partition);
        var time = QueueWholeFlowStorage.Clock(fixture.Store).AddSeconds(1);
        var first = new ReceiveSubscriptionRequest(Guid.NewGuid(), group, MaxEvents: 3, LeaseSeconds: 1);
        var firstOperation = Create(OperationKind.ReceiveSubscription, first, first.RequestId, time);
        var firstResult = fixture.Database.Apply(firstOperation);
        var deliveries = firstResult.Get<ReceiveSubscriptionResult>().Deliveries;
        await Assert.That(deliveries.Select(d => d.Event.Position)).IsEquivalentTo(
            new long[] { 1, 2, 3 }, CollectionOrdering.Matching);
        await NativeReplayResultAssertions.Same<ReceiveSubscriptionResult>(fixture.Database.Apply(firstOperation), firstResult);
        var claims = new List<(ReplicatedOperation Operation, OperationResult Result, byte[] Saved)>
        { (firstOperation, firstResult, SubscriptionProcessingAtomicAssertions.OutcomeBytes(fixture.Store, group, first.RequestId)) };
        var receipts = new List<(ReplicatedOperation Operation, OperationResult Result, byte[] Saved)>();
        foreach (var delivery in deliveries.Skip(1))
        { await AckAsync(fixture.Database, fixture.Store, group, delivery, time.AddMilliseconds(500), receipts); }
        var retry = new ReceiveSubscriptionRequest(Guid.NewGuid(), group, MaxEvents: 3, LeaseSeconds: 1);
        var retryOperation = Create(OperationKind.ReceiveSubscription, retry, retry.RequestId, time.AddSeconds(2));
        var retryResult = fixture.Database.Apply(retryOperation);
        await NativeReplayResultAssertions.Same<ReceiveSubscriptionResult>(fixture.Database.Apply(retryOperation), retryResult);
        claims.Add((retryOperation, retryResult, SubscriptionProcessingAtomicAssertions.OutcomeBytes(fixture.Store, group, retry.RequestId)));
        var reclaimed = await Assert.That(retryResult.Get<ReceiveSubscriptionResult>().Deliveries).HasSingleItem();
        await Assert.That(reclaimed).IsEqualTo(new SubscriptionDelivery(deliveries[0].Event,
            reclaimed.Token, 2L, time.AddSeconds(3), 2));
        await Assert.That(reclaimed.Token).IsNotEqualTo(deliveries[0].Token);
        var park = new ReceiveSubscriptionRequest(Guid.NewGuid(), group, MaxEvents: 3, LeaseSeconds: 1);
        var parkOperation = Create(OperationKind.ReceiveSubscription, park, park.RequestId, time.AddSeconds(4));
        var parked = fixture.Database.Apply(parkOperation);
        await Assert.That(parked.Get<ReceiveSubscriptionResult>().Deliveries).IsEmpty();
        await SubscriptionParkedRecoveryAssertions.StatusAsync(fixture.Database, group, policy, 1, 0, 3, 3, true, "AttemptsExhausted");
        var history = SubscriptionProcessingAtomicAssertions.OutcomeBytes(fixture.Store, group, park.RequestId);
        await SubscriptionParkedRecoveryAssertions.ResumeRefusalAsync(fixture.Database, fixture.Store, group, time.AddSeconds(5), receipts);
        await SubscriptionParkedRecoveryAssertions.ReplayAsync(fixture.Database, fixture.Store, group, parkOperation, parked, history);
        var bytes = QueueWholeFlowStorage.Bytes(fixture.Store);
        var position = fixture.Store.Position;
        var identity = NativeSerialization.Serialize(fixture.Store.Identity);
        fixture.Store.Dispose();
        token.ThrowIfCancellationRequested();
        using var cold = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(),
            UnitExecutionOptions.PointCacheExecution());
        var db = QueueWholeFlowStorage.Open(cold);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(cold, bytes, position, identity);
        await SubscriptionParkedRecoveryAssertions.StatusAsync(db, group, policy, 1, 0, 3, 3, true, "AttemptsExhausted");
        await SubscriptionCheckpointAuthorityAssertions.SourceAsync(db, cold, group.Source, source);
        await SubscriptionParkedRecoveryAssertions.ReplayAsync(db, cold, group, parkOperation, parked, history);
        foreach (var receipt in receipts)
        { await SubscriptionParkedRecoveryAssertions.ReplayAsync(db, cold, group, receipt.Operation, receipt.Result, receipt.Saved); }
        await RecoverAsync(db, cold, fixture.Directory, group, policy, parkOperation, parked,
            history, deliveries, receipts, claims, time, source, token);
    }

    private static async Task RecoverAsync(DatabaseEngine db, ZoneTreeStore store, string directory,
        SubscriptionRef group, SubscriptionPolicy policy, ReplicatedOperation park, OperationResult parked,
        byte[] history, System.Collections.Immutable.ImmutableArray<SubscriptionDelivery> oldDeliveries,
        List<(ReplicatedOperation Operation, OperationResult Result, byte[] Saved)> receipts,
        List<(ReplicatedOperation Operation, OperationResult Result, byte[] Saved)> claims,
        DateTimeOffset time, string[] source, CancellationToken token)
    {
        var seek = new SeekSubscriptionRequest(Guid.NewGuid(), group, 1, SubscriptionStart.FromBeginning);
        var seekOperation = Create(OperationKind.SeekSubscription, seek, seek.CommandId, time.AddSeconds(6));
        var sought = db.Apply(seekOperation);
        await Assert.That(JsonSerializer.Serialize(sought.Get<SubscriptionInfo>(), JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(db.GetSubscription("root", group), JsonDefaults.Options));
        await SubscriptionParkedRecoveryAssertions.StatusAsync(db, group, policy, 2, 0, 0, 3, true, null);
        receipts.Add((seekOperation, sought, SubscriptionProcessingAtomicAssertions.OutcomeBytes(store, group, seek.CommandId)));
        await SubscriptionParkedRecoveryAssertions.NoWindowAsync(store, group);
        var resume = new SetSubscriptionPausedRequest(Guid.NewGuid(), group, 2, false);
        var resumeOperation = Create(OperationKind.SetSubscriptionPaused, resume, resume.CommandId, time.AddSeconds(7));
        var resumed = db.Apply(resumeOperation);
        await Assert.That(JsonSerializer.Serialize(resumed.Get<SubscriptionInfo>(), JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(db.GetSubscription("root", group), JsonDefaults.Options));
        await SubscriptionParkedRecoveryAssertions.StatusAsync(db, group, policy, 2, 0, 0, 3, false, null);
        receipts.Add((resumeOperation, resumed, SubscriptionProcessingAtomicAssertions.OutcomeBytes(store, group, resume.CommandId)));
        await SubscriptionParkedRecoveryAssertions.OldReceiveAsync(db, store, group, park, parked, history);
        foreach (var old in claims)
        {
            await SubscriptionParkedRecoveryAssertions.OldReceiveAsync(db, store, group,
            old.Operation, old.Result, old.Saved);
        }
        await SubscriptionParkedRecoveryAssertions.FencedAsync(db, store, group, oldDeliveries[0].Token, time.AddSeconds(7));
        var request = new ReceiveSubscriptionRequest(Guid.NewGuid(), group, MaxEvents: 3, LeaseSeconds: 1);
        var operation = Create(OperationKind.ReceiveSubscription, request, request.RequestId, time.AddSeconds(8));
        var original = db.Apply(operation);
        var deliveries = original.Get<ReceiveSubscriptionResult>().Deliveries;
        await Assert.That(deliveries.Length).IsEqualTo(3);
        for (var index = 0; index < deliveries.Length; index++)
        {
            await Assert.That(deliveries[index]).IsEqualTo(new SubscriptionDelivery(
                oldDeliveries[index].Event, deliveries[index].Token, 1L, time.AddSeconds(9), 1));
            await Assert.That(deliveries[index].Token).IsNotEqualTo(oldDeliveries[index].Token);
        }
        await NativeReplayResultAssertions.Same<ReceiveSubscriptionResult>(db.Apply(operation), original);
        await AckAsync(db, store, group, deliveries[2], time.AddMilliseconds(8500), receipts);
        await SubscriptionParkedRecoveryAssertions.StatusAsync(db, group, policy, 2, 0, 3, 3, false, null);
        await AckAsync(db, store, group, deliveries[0], time.AddMilliseconds(8500), receipts);
        await SubscriptionParkedRecoveryAssertions.StatusAsync(db, group, policy, 2, 1, 3, 3, false, null);
        await AckAsync(db, store, group, deliveries[1], time.AddMilliseconds(8500), receipts);
        await SubscriptionParkedRecoveryAssertions.StatusAsync(db, group, policy, 2, 3, 3, 3, false, null);
        await SubscriptionParkedRecoveryAssertions.NoWindowAsync(store, group);
        await ColdAsync(store, directory, group, policy, park, parked, history, receipts, claims, source, time, token);
    }

    private static async Task ColdAsync(ZoneTreeStore store, string directory, SubscriptionRef group,
        SubscriptionPolicy policy, ReplicatedOperation park, OperationResult parked, byte[] history,
        List<(ReplicatedOperation Operation, OperationResult Result, byte[] Saved)> receipts,
        List<(ReplicatedOperation Operation, OperationResult Result, byte[] Saved)> claims,
        string[] source, DateTimeOffset time, CancellationToken token)
    {
        var bytes = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = NativeSerialization.Serialize(store.Identity);
        store.Dispose();
        token.ThrowIfCancellationRequested();
        using var cold = new ZoneTreeStore(new(directory), UnitExecutionOptions.StorageExecution(),
            UnitExecutionOptions.PointCacheExecution());
        var db = QueueWholeFlowStorage.Open(cold);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(cold, bytes, position, identity);
        await SubscriptionParkedRecoveryAssertions.StatusAsync(db, group, policy, 2, 3, 3, 3, false, null);
        await SubscriptionParkedRecoveryAssertions.NoWindowAsync(cold, group);
        await SubscriptionCheckpointAuthorityAssertions.SourceAsync(db, cold, group.Source, source);
        await SubscriptionParkedRecoveryAssertions.OldReceiveAsync(db, cold, group, park, parked, history);
        foreach (var old in claims)
        {
            await SubscriptionParkedRecoveryAssertions.OldReceiveAsync(db, cold, group,
            old.Operation, old.Result, old.Saved);
        }
        foreach (var receipt in receipts)
        {
            await SubscriptionParkedRecoveryAssertions.ReplayAsync(db, cold, group,
            receipt.Operation, receipt.Result, receipt.Saved);
        }
        await HealthyAsync(db, cold, group, policy, time.AddSeconds(10), token);
    }

    private static async Task HealthyAsync(DatabaseEngine db, ZoneTreeStore store, SubscriptionRef group,
        SubscriptionPolicy policy, DateTimeOffset time, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var publish = new CommandRequest(Guid.NewGuid(), group.Source.Partition,
            [new PublishTopic("topic", [new("event4", "Created", "{\"n\":4}")])]);
        db.Apply(Create(OperationKind.Batch, publish, publish.CommandId, time)).Get<CommitReceipt>();
        var request = new ReceiveSubscriptionRequest(Guid.NewGuid(), group, MaxEvents: 1, LeaseSeconds: 1);
        var operation = Create(OperationKind.ReceiveSubscription, request, request.RequestId, time);
        var original = db.Apply(operation);
        var delivery = await Assert.That(original.Get<ReceiveSubscriptionResult>().Deliveries).HasSingleItem();
        await Assert.That(delivery.Event.Position).IsEqualTo(4L);
        await Assert.That(delivery.Event.Data).IsEqualTo(new EventData("event4", "Created", "{\"n\":4}"));
        await Assert.That(delivery.Attempt).IsEqualTo(1);
        await Assert.That(delivery.LeaseVersion).IsEqualTo(1L);
        await NativeReplayResultAssertions.Same<ReceiveSubscriptionResult>(db.Apply(operation), original);
        var receipts = new List<(ReplicatedOperation Operation, OperationResult Result, byte[] Saved)>();
        await AckAsync(db, store, group, delivery, time.AddMilliseconds(500), receipts);
        await SubscriptionParkedRecoveryAssertions.StatusAsync(db, group, policy, 2, 4, 4, 4, false, null);
        await SubscriptionParkedRecoveryAssertions.NoWindowAsync(store, group);
        await SubscriptionCheckpointAuthorityAssertions.SettledClaimAsync(db, store, request, original, token);
        foreach (var receipt in receipts)
        {
            await SubscriptionParkedRecoveryAssertions.ReplayAsync(db, store, group,
            receipt.Operation, receipt.Result, receipt.Saved);
        }
    }

    private static async Task AckAsync(DatabaseEngine db, ZoneTreeStore store, SubscriptionRef group,
        SubscriptionDelivery delivery, DateTimeOffset time,
        List<(ReplicatedOperation Operation, OperationResult Result, byte[] Saved)> receipts)
    {
        var ack = new SubscriptionDeliveryCommand(Guid.NewGuid(), group, delivery.Token, DeliveryAction.Ack);
        var operation = Create(OperationKind.SubscriptionDelivery, ack, ack.CommandId, time);
        var original = db.Apply(operation);
        await SubscriptionCheckpointAuthorityAssertions.ReceiptAsync(original, ack, delivery.Event.Position);
        receipts.Add((operation, original, SubscriptionProcessingAtomicAssertions.OutcomeBytes(store, group, ack.CommandId)));
    }

    private static ReplicatedOperation Create<T>(OperationKind kind, T payload, Guid id, DateTimeOffset time)
        => new(id, kind, "root", time, JsonSerializer.Serialize(payload, JsonDefaults.Options));
}

