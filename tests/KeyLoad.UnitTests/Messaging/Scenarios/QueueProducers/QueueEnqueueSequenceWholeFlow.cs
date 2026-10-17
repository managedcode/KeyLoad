using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.StorageRecovery.Assertions;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueEnqueueSequenceWholeFlow
{
    internal static async Task RunAsync(bool strict, bool scheduled, CancellationToken token)
    {
        using var fixture = new TestDatabase();
        fixture.Configure(QueueEnqueueColdProtocol.Collection, ResourceKind.Collection);
        fixture.Configure(QueueEnqueueColdProtocol.Queue, ResourceKind.WorkQueue, queuePolicy: new()
        {
            OrderingProfile = strict ? QueueOrderingProfile.StrictPerKey : QueueOrderingProfile.CompetingConsumers
        });
        var lane = new QueueLaneRef(fixture.Partition, QueueEnqueueColdProtocol.Queue);
        var key = KeySpace.Partition(QueueEnqueueColdProtocol.CountersFamily, fixture.Partition, lane.Queue);
        var counters = new QueueCounters(QueueEnqueueAuthorityProtocol.Initial, QueueEnqueueAuthorityProtocol.Initial, QueueEnqueueAuthorityProtocol.Initial, QueueEnqueueAuthorityProtocol.Initial, strict ? QueueEnqueueAuthorityProtocol.Initial : long.MaxValue)
        {
            NextOrderSequence = strict ? long.MaxValue : QueueEnqueueAuthorityProtocol.Initial
        };
        fixture.Store.Commit((tx, _) =>
        {
            tx.PutRecord(key, counters);
            return true;
        });
        var now = QueueWholeFlowStorage.Clock(fixture.Store);
        var due = scheduled ? now.AddHours(QueueEnqueueColdProtocol.FutureScheduledHours) : (DateTimeOffset?)null;
        var message = new EnqueueMessage(lane.Queue, QueueEnqueueAuthorityProtocol.SequenceOriginal, QueueEnqueueColdProtocol.ReadyJson, QueueEnqueueColdProtocol.EmptyHeaders, NotBefore: due, OrderingKey: strict ? QueueEnqueueAuthorityProtocol.OrderingKey : null);
        var command = new CommandRequest(Guid.NewGuid(), lane.Partition,
            [new PutDocument(QueueEnqueueColdProtocol.Collection, QueueEnqueueColdProtocol.Document, QueueEnqueueColdProtocol.ReadyJson), message]);
        var operation = QueueStoredCapacitySetup.Operation(OperationKind.Batch, command, command.CommandId, now);
        var domain = QueueClaimVersionExhaustionAssertions.Domain(fixture.Store, lane);
        if (strict)
        {
            await QueueEnqueueSequenceRefusals.InvalidKeysAsync(fixture.Database, fixture.Store, lane, command, now);
        }
        var expected = await QueueStoredCapacityReceipts.NextAsync(fixture.Database, fixture.Store, fixture.Partition);
        var result = fixture.Database.Apply(operation);
        var succeeds = !strict && scheduled;
        await QueueEnqueueSequenceAssertions.ResultAsync(fixture.Database, fixture.Store, lane, command,
            message, counters, result, expected, succeeds, domain);
        await QueueStoredCapacityReceipts.FailureAsync(fixture.Database, fixture.Store, fixture.Partition, operation, result);
        var image = QueueWholeFlowStorage.Bytes(fixture.Store);
        var position = fixture.Store.Position;
        var identity = fixture.Store.Identity;
        fixture.Store.Dispose();
        using var cold = new ZoneTreeStore(new(fixture.Directory), fixture.StorageExecution, UnitExecutionOptions.PointCacheExecution());
        var owner = fixture.CreateConfiguredDatabase(cold);
        await NativeReadCutStoreStateAssertions.AssertIdentityAndPositionAsync(cold, identity, position);
        await QueueStoredCapacityAssertions.ImageAsync(cold, position, image);
        await QueueStoredCapacityReceipts.FailureAsync(owner, cold, fixture.Partition, operation, result);
        await QueueEnqueueSequenceAssertions.ResultAsync(owner, cold, lane, command,
            message, counters, result, expected, succeeds, domain);
        var healthy = await ContinueAsync(owner, cold, lane, key, operation, result, strict, succeeds, token);
        var final = QueueWholeFlowStorage.Bytes(cold);
        var finalPosition = cold.Position;
        cold.Dispose();
        using var second = new ZoneTreeStore(new(fixture.Directory), fixture.StorageExecution, UnitExecutionOptions.PointCacheExecution());
        var restored = fixture.CreateConfiguredDatabase(second);
        await NativeReadCutStoreStateAssertions.AssertIdentityAndPositionAsync(second, identity, finalPosition);
        await QueueStoredCapacityAssertions.ImageAsync(second, finalPosition, final);
        await QueueStoredCapacityReceipts.FailureAsync(restored, second, fixture.Partition, operation, result);
        await QueueStoredCapacityReceipts.FailureAsync(restored, second, fixture.Partition, healthy.Operation, healthy.Result);
    }

    private static async Task<(ReplicatedOperation Operation, OperationResult Result)> ContinueAsync(DatabaseEngine owner, ZoneTreeStore store, QueueLaneRef lane, byte[] key,
        ReplicatedOperation original, OperationResult result, bool strict, bool scheduledSuccess, CancellationToken token)
    {
        var before = store.Read(view => view.GetRecord<QueueCounters>(key))!;
        store.Commit((tx, _) =>
        {
            tx.PutRecord(key, before with
            {
                NextReadySequence = QueueEnqueueAuthorityProtocol.Initial,
                NextOrderSequence = QueueEnqueueAuthorityProtocol.Initial
            });
            return true;
        });
        await QueueStoredCapacityReceipts.FailureAsync(owner, store, lane.Partition, original, result);
        var message = new EnqueueMessage(lane.Queue, QueueEnqueueColdProtocol.Healthy, QueueEnqueueAuthorityProtocol.SequenceHealthyJson, QueueEnqueueColdProtocol.EmptyHeaders, OrderingKey: strict ? QueueEnqueueAuthorityProtocol.OrderingKey : null);
        var fresh = new CommandRequest(Guid.NewGuid(), lane.Partition,
            [new PutDocument(QueueEnqueueColdProtocol.Collection, QueueEnqueueColdProtocol.Healthy, QueueEnqueueAuthorityProtocol.SequenceHealthyJson), message]);
        var time = QueueWholeFlowStorage.Clock(store);
        var operation = QueueStoredCapacitySetup.Operation(OperationKind.Batch, fresh, fresh.CommandId, time);
        var expected = await QueueStoredCapacityReceipts.NextAsync(owner, store, lane.Partition);
        var produced = owner.Apply(operation);
        await QueueStoredCapacityReceipts.ReceiptAsync(produced, fresh.CommandId, expected,
            [new(QueueEnqueueColdProtocol.PutKind, QueueEnqueueColdProtocol.Collection, QueueEnqueueColdProtocol.Healthy, QueueEnqueueAuthorityProtocol.First), new(QueueEnqueueColdProtocol.EnqueueKind, lane.Queue, QueueEnqueueColdProtocol.Healthy, QueueEnqueueAuthorityProtocol.First)]);
        await QueueEnqueueSequenceAssertions.MessageAsync(owner, store, lane, message,
            new(QueueEnqueueColdProtocol.Healthy, MessageState.Ready, QueueEnqueueAuthorityProtocol.Initial, QueueEnqueueAuthorityProtocol.First, QueueEnqueueAuthorityProtocol.First, null, null)
            {
                EnqueueSequence = strict ? QueueEnqueueAuthorityProtocol.First : QueueEnqueueAuthorityProtocol.Initial,
                ActiveOrderSequence = strict ? QueueEnqueueAuthorityProtocol.First : QueueEnqueueAuthorityProtocol.Initial
            });
        var bytes = NativeSerialization.Serialize(new MessageBody(message.MessageId, message.PayloadJson,
            message.HeadersJson, message.OrderingKey, JsonData.Fingerprint(message))).LongLength;
        await Assert.That(store.Read(view => view.GetRecord<QueueCounters>(key))).IsEqualTo(new QueueCounters(
            scheduledSuccess ? QueueEnqueueAuthorityProtocol.Second : QueueEnqueueAuthorityProtocol.First, before.StoredBytes + bytes, QueueEnqueueAuthorityProtocol.Initial, QueueEnqueueAuthorityProtocol.Initial, QueueEnqueueAuthorityProtocol.First)
        {
            NextOrderSequence = strict ? QueueEnqueueAuthorityProtocol.First : QueueEnqueueAuthorityProtocol.Initial
        });
        await QueueStoredCapacityReceipts.FailureAsync(owner, store, lane.Partition, operation, produced);
        await QueueStoredCapacityReceipts.FailureAsync(owner, store, lane.Partition, original, result);
        token.ThrowIfCancellationRequested();
        return (operation, produced);
    }
}
