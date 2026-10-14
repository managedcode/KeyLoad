using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class SubscriptionInboxConflictFlow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        using var fixture = new TestDatabase();
        fixture.Configure("topic", ResourceKind.Topic);
        fixture.Configure("orders", ResourceKind.Collection);
        var group = SubscriptionTestActions.Configure(fixture, policy: new()
        {
            MaxWindow = 3
        });
        var producedAt = QueueWholeFlowStorage.Clock(fixture.Store).AddSeconds(1);
        var publish = new CommandRequest(Guid.NewGuid(), group.Source.Partition,
            [new PublishTopic("topic", [new("event1", "Created", "{\"n\":1}"),
                new("event2", "Created", "{\"n\":2}"), new("event3", "Created", "{\"n\":3}")])]);
        var producer = Create(OperationKind.Batch, publish, publish.CommandId, producedAt);
        var producerResult = fixture.Database.Apply(producer);
        producerResult.Get<CommitReceipt>();
        await NativeReplayResultAssertions.Same<CommitReceipt>(fixture.Database.Apply(producer), producerResult);
        var source = SubscriptionCheckpointAuthorityAssertions.SourceImage(fixture.Store, group.Source.Partition);
        var time = producedAt.AddSeconds(1);
        var receive = new ReceiveSubscriptionRequest(Guid.NewGuid(), group, MaxEvents: 3);
        var claim = Create(OperationKind.ReceiveSubscription, receive, receive.RequestId, time);
        var originalClaim = fixture.Database.Apply(claim);
        var deliveries = originalClaim.Get<ReceiveSubscriptionResult>().Deliveries;
        await NativeReplayResultAssertions.Same<ReceiveSubscriptionResult>(fixture.Database.Apply(claim), originalClaim);
        await SubscriptionInboxConflictAssertions.InitialClaimsAsync(fixture.Database, group, deliveries, producedAt);
        var request = new SubscriptionProcessingRequest(Guid.NewGuid(), group, deliveries[0].Token, "worker", 1,
            [new PutDocument("orders", "first", "{\"n\":1}", 0), new PutDocument("orders", "second", "{\"n\":2}", 0)]);
        var operation = Create(OperationKind.SubscriptionProcessing, request, request.CommandId, time.AddSeconds(1));
        var result = fixture.Database.Apply(operation);
        await SubscriptionProcessingAtomicAssertions.SuccessAsync(fixture.Database, group, request, result);
        var history = new List<(ReplicatedOperation Operation, OperationResult Result, byte[] Saved)>
        { (operation, result, SubscriptionProcessingAtomicAssertions.OutcomeBytes(fixture.Store, group, operation.Id)) };
        var claims = new List<(ReplicatedOperation Operation, OperationResult Result, byte[] Saved)>
        { (claim, originalClaim, SubscriptionProcessingAtomicAssertions.OutcomeBytes(fixture.Store, group, claim.Id)) };
        var inbox = SubscriptionInboxConflictAssertions.InboxBytes(fixture.Store, group);
        await SubscriptionInboxConflictAssertions.ReplayAsync(fixture.Database, fixture.Store, group, history[0]);
        var seek = new SeekSubscriptionRequest(Guid.NewGuid(), group, 1, SubscriptionStart.FromBeginning);
        Add(fixture.Database, fixture.Store, group, Create(OperationKind.SeekSubscription, seek, seek.CommandId, time.AddSeconds(2)), history);
        await SubscriptionParkedRecoveryAssertions.StatusAsync(fixture.Database, group, new()
        {
            MaxWindow = 3
        }, 2, 0, 0, 3, true, null);
        var resume = new SetSubscriptionPausedRequest(Guid.NewGuid(), group, 2, false);
        Add(fixture.Database, fixture.Store, group, Create(OperationKind.SetSubscriptionPaused, resume, resume.CommandId, time.AddSeconds(2)), history);
        await SubscriptionParkedRecoveryAssertions.StatusAsync(fixture.Database, group, new()
        {
            MaxWindow = 3
        }, 2, 0, 0, 3, false, null);
        var current = new ReceiveSubscriptionRequest(Guid.NewGuid(), group, MaxEvents: 3);
        var currentOperation = Create(OperationKind.ReceiveSubscription, current, current.RequestId, time.AddSeconds(3));
        var currentResult = fixture.Database.Apply(currentOperation);
        var currentDeliveries = currentResult.Get<ReceiveSubscriptionResult>().Deliveries;
        await SubscriptionInboxConflictAssertions.ClaimsAsync(currentDeliveries, deliveries, time.AddSeconds(33));
        await NativeReplayResultAssertions.Same<ReceiveSubscriptionResult>(fixture.Database.Apply(currentOperation), currentResult);
        claims.Add((currentOperation, currentResult, SubscriptionProcessingAtomicAssertions.OutcomeBytes(fixture.Store, group, current.RequestId)));
        var conflict = request with
        {
            CommandId = Guid.NewGuid(),
            Token = currentDeliveries[0].Token,
            Effects = [new PutDocument("orders", "first", "{\"n\":99}", 1), new PutDocument("orders", "second", "{\"n\":2}", 0)]
        };
        var failed = Create(OperationKind.SubscriptionProcessing, conflict, conflict.CommandId, time.AddSeconds(4));
        await SubscriptionInboxConflictAssertions.RefusalAsync(fixture.Database, fixture.Store, group, failed, inbox, source, history);
        await ColdAsync(fixture, group, request, claims, history, inbox, source, token);
    }

    private static async Task ColdAsync(TestDatabase fixture, SubscriptionRef group, SubscriptionProcessingRequest original,
        List<(ReplicatedOperation Operation, OperationResult Result, byte[] Saved)> claims,
        List<(ReplicatedOperation Operation, OperationResult Result, byte[] Saved)> history,
        byte[] inbox, string[] source, CancellationToken token)
    {
        var cut = QueueWholeFlowStorage.Bytes(fixture.Store);
        var position = fixture.Store.Position;
        var identity = NativeSerialization.Serialize(fixture.Store.Identity);
        fixture.Store.Dispose();
        token.ThrowIfCancellationRequested();
        using var cold = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var db = QueueWholeFlowStorage.Open(cold);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(cold, cut, position, identity);
        await SubscriptionInboxConflictAssertions.PreservedAsync(db, cold, group, inbox, source);
        foreach (var entry in history.Skip(1))
        {
            await SubscriptionInboxConflictAssertions.ReplayAsync(db, cold, group, entry);
        }
        await SubscriptionInboxConflictAssertions.FencedOriginalProcessingAsync(db, cold, group, history[0]);
        await SubscriptionParkedRecoveryAssertions.OldReceiveAsync(db, cold, group, claims[0].Operation, claims[0].Result, claims[0].Saved);
        await NativeReplayResultAssertions.Same<ReceiveSubscriptionResult>(db.Apply(claims[1].Operation), claims[1].Result);
        var deliveries = claims[1].Result.Get<ReceiveSubscriptionResult>().Deliveries;
        var repair = original with
        {
            CommandId = Guid.NewGuid(),
            Token = deliveries[0].Token
        };
        var repairedOperation = Create(OperationKind.SubscriptionProcessing, repair, repair.CommandId,
            QueueWholeFlowStorage.Clock(cold).AddSeconds(1));
        var repaired = db.Apply(repairedOperation);
        await SubscriptionInboxConflictAssertions.RepairedAsync(db, cold, group, repair, repaired, history[0].Result, inbox, source);
        history.Add((repairedOperation, repaired, SubscriptionProcessingAtomicAssertions.OutcomeBytes(cold, group, repairedOperation.Id)));
        await SubscriptionInboxConflictAssertions.ReplayAsync(db, cold, group, history[3]);
        foreach (var index in new[] { 2, 1 })
        {
            var ack = new SubscriptionDeliveryCommand(Guid.NewGuid(), group, deliveries[index].Token, DeliveryAction.Ack);
            var operation = Create(OperationKind.SubscriptionDelivery, ack, ack.CommandId, QueueWholeFlowStorage.Clock(cold).AddSeconds(1));
            var result = db.Apply(operation);
            await SubscriptionCheckpointAuthorityAssertions.ReceiptAsync(result, ack, deliveries[index].Event.Position);
            history.Add((operation, result, SubscriptionProcessingAtomicAssertions.OutcomeBytes(cold, group, operation.Id)));
            await SubscriptionParkedRecoveryAssertions.StatusAsync(db, group, new()
            {
                MaxWindow = 3
            }, 2, index == 2 ? 1 : 3, 3, 3, false, null);
        }
        await SubscriptionParkedRecoveryAssertions.NoWindowAsync(cold, group);
        var current = JsonSerializer.Deserialize<ReceiveSubscriptionRequest>(claims[1].Operation.PayloadJson, JsonDefaults.Options)
            ?? throw new InvalidOperationException("The original current Receive request is absent.");
        await SubscriptionCheckpointAuthorityAssertions.SettledClaimAsync(db, cold, current, claims[1].Result, token);
        await SecondColdAsync(cold, fixture.Directory, group, claims, history, inbox, source, current, token);
    }

    private static async Task SecondColdAsync(ZoneTreeStore store, string directory, SubscriptionRef group,
        List<(ReplicatedOperation Operation, OperationResult Result, byte[] Saved)> claims,
        List<(ReplicatedOperation Operation, OperationResult Result, byte[] Saved)> history,
        byte[] inbox, string[] source, ReceiveSubscriptionRequest current, CancellationToken token)
    {
        var cut = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = NativeSerialization.Serialize(store.Identity);
        store.Dispose();
        token.ThrowIfCancellationRequested();
        using var cold = new ZoneTreeStore(new(directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var db = QueueWholeFlowStorage.Open(cold);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(cold, cut, position, identity);
        await SubscriptionInboxConflictAssertions.PreservedAsync(db, cold, group, inbox, source);
        foreach (var entry in history.Skip(1))
        {
            await SubscriptionInboxConflictAssertions.ReplayAsync(db, cold, group, entry);
        }
        await SubscriptionInboxConflictAssertions.FencedOriginalProcessingAsync(db, cold, group, history[0]);
        await SubscriptionParkedRecoveryAssertions.OldReceiveAsync(db, cold, group, claims[0].Operation, claims[0].Result, claims[0].Saved);
        await SubscriptionCheckpointAuthorityAssertions.SettledClaimAsync(db, cold, current, claims[1].Result, token);
        await SubscriptionParkedRecoveryAssertions.StatusAsync(db, group, new()
        {
            MaxWindow = 3
        }, 2, 3, 3, 3, false, null);
        await SubscriptionParkedRecoveryAssertions.NoWindowAsync(cold, group);
        await HealthyAsync(db, cold, group, token);
        await SubscriptionInboxConflictAssertions.PreservedDocumentsAndInboxAsync(db, cold, group, inbox);
    }

    private static async Task HealthyAsync(DatabaseEngine db, ZoneTreeStore store, SubscriptionRef group, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var time = QueueWholeFlowStorage.Clock(store).AddSeconds(1);
        var publish = new CommandRequest(Guid.NewGuid(), group.Source.Partition,
            [new PublishTopic("topic", [new("event4", "Created", "{\"n\":4}")])]);
        db.Apply(Create(OperationKind.Batch, publish, publish.CommandId, time)).Get<CommitReceipt>();
        var request = new ReceiveSubscriptionRequest(Guid.NewGuid(), group, MaxEvents: 1);
        var operation = Create(OperationKind.ReceiveSubscription, request, request.RequestId, time);
        var original = db.Apply(operation);
        var delivery = await Assert.That(original.Get<ReceiveSubscriptionResult>().Deliveries).HasSingleItem();
        var expected = new SubscriptionDelivery(new SourceEventRecord(group.Source, 4L, 4L,
            new("event4", "Created", "{\"n\":4}"), time), delivery.Token, 1L, time.AddSeconds(30), 1);
        await Assert.That(JsonSerializer.Serialize(delivery, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));
        await NativeReplayResultAssertions.Same<ReceiveSubscriptionResult>(db.Apply(operation), original);
        var ack = new SubscriptionDeliveryCommand(Guid.NewGuid(), group, delivery.Token, DeliveryAction.Ack);
        var ackOperation = Create(OperationKind.SubscriptionDelivery, ack, ack.CommandId, time.AddSeconds(1));
        var receipt = db.Apply(ackOperation);
        await SubscriptionCheckpointAuthorityAssertions.ReceiptAsync(receipt, ack, 4);
        await SubscriptionParkedRecoveryAssertions.StatusAsync(db, group, new()
        {
            MaxWindow = 3
        }, 2, 4, 4, 4, false, null);
        await SubscriptionParkedRecoveryAssertions.NoWindowAsync(store, group);
        await SubscriptionCheckpointAuthorityAssertions.SettledClaimAsync(db, store, request, original, token);
        await SubscriptionCheckpointAuthorityAssertions.ReplayAsync(db, store, ack, receipt, token);
    }

    private static void Add(DatabaseEngine db, ZoneTreeStore store, SubscriptionRef group, ReplicatedOperation operation,
        List<(ReplicatedOperation Operation, OperationResult Result, byte[] Saved)> history)
    {
        var result = db.Apply(operation);
        result.Get<SubscriptionInfo>();
        history.Add((operation, result, SubscriptionProcessingAtomicAssertions.OutcomeBytes(store, group, operation.Id)));
    }

    private static ReplicatedOperation Create<T>(OperationKind kind, T payload, Guid id, DateTimeOffset time)
        => new(id, kind, "root", time, JsonSerializer.Serialize(payload, JsonDefaults.Options));
}

