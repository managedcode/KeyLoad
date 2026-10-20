using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class AppliedOutcomeDefinitiveRefusalAssertions
{
    internal static async Task ConflictAsync(DatabaseEngine db, ZoneTreeStore store, Command<DatabaseCommand> original,
        Command<DatabaseCommand> changed, Result<object> committed)
    {
        var bytes = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = OrleansSerialization.Serialize(store.Identity);
        for (var repeat = 0; repeat < 2; repeat++)
        {
            var local = db.Apply(changed);
            await RefusalAsync(local, ErrorCode.Conflict, "The command ID was already used with different content.");
            var resolved = db.ResolveAppliedSubmissionOutcome(changed);
            await RefusalAsync(resolved, ErrorCode.Conflict, "The command ID was already used with different content.");
            await ReplayResultAssertions.Same<CommitReceipt>(db.ResolveAppliedSubmissionOutcome(original), committed);
            await ReplayResultAssertions.Same<CommitReceipt>(db.Apply(original), committed);
            await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, bytes, position, identity);
        }
    }

    internal static async Task BoundariesAsync(DatabaseEngine db, ZoneTreeStore store, PartitionRef partition, Command<DatabaseCommand> original,
        Command<DatabaseCommand> changed, Result<object> committed)
    {
        var key = KeySpace.PartitionOutcome(partition,
            AppliedOutcomeDefinitiveRefusalFlow.Worker, original.CommandId);
        var saved = store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException("The genuine original ACK outcome is absent.");
        var outcome = OrleansSerialization.Deserialize<StoredOutcome>(saved);
        try
        {
            Put(store, key, OrleansSerialization.Serialize(outcome with
            {
                Incarnation = Guid.NewGuid()
            }));
            await ReadBoundaryAsync(db, store, original, ErrorCode.TokenInvalidated,
                "The command belongs to an earlier incarnation.");
            Put(store, key, OrleansSerialization.Serialize(outcome with
            {
                Partition = outcome.Partition! with
                {
                    PartitionKey = "foreign-outcome-partition"
                }
            }));
            await FatalBoundaryAsync(db, store, original, ErrorCode.Corruption);
        }
        finally
        {
            Put(store, key, saved);
        }
        var request = new ReceiveRequest(Guid.NewGuid(), new(partition, AppliedOutcomeDefinitiveRefusalFlow.Queue));
        var missing = AppliedOutcomeDefinitiveRefusalFlow.Issue(db, OperationKind.Receive, request, request.RequestId);
        await FatalBoundaryAsync(db, store, missing, ErrorCode.RecoveryRequired);
        await ConflictAsync(db, store, original, changed, committed);
    }

    private static async Task ReadBoundaryAsync(DatabaseEngine db, ZoneTreeStore store,
        Command<DatabaseCommand> operation, ErrorCode code, string detail)
    {
        var bytes = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = OrleansSerialization.Serialize(store.Identity);
        await RefusalAsync(db.ResolveAppliedSubmissionOutcome(operation), code, detail);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, bytes, position, identity);
    }

    private static async Task FatalBoundaryAsync(DatabaseEngine db, ZoneTreeStore store,
        Command<DatabaseCommand> operation, ErrorCode code)
    {
        var bytes = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = OrleansSerialization.Serialize(store.Identity);
        var refusal = Assert.ThrowsExactly<KeyLoadException>(() => db.ResolveAppliedSubmissionOutcome(operation));
        await Assert.That(refusal.Code).IsEqualTo(code);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, bytes, position, identity);
    }

    private static async Task RefusalAsync(Result<object> result, ErrorCode code, string detail)
    {
        await Assert.That(Errors.Code(result.Problem)).IsEqualTo(code);
        await Assert.That(result.Problem?.Detail).IsEqualTo(detail);

        await Assert.That(result.Value).IsNull();
    }

    internal static async Task ReceiptAsync(Result<object> result, Guid id, CommitToken token, string message)
    {
        var expected = new CommitReceipt(id, token, [new("Ack", AppliedOutcomeDefinitiveRefusalFlow.Queue, message, 3L)], DurabilityProfile.ProcessDurable, 1);
        await Assert.That(OrleansSerialization.Serialize(result.Get<CommitReceipt>()).AsSpan()
            .SequenceEqual(OrleansSerialization.Serialize(expected))).IsTrue();
    }

    internal static async Task CurrentAsync(DatabaseEngine db, ZoneTreeStore store, QueueLaneRef lane, bool completed)
    {
        var original = new MessageMetadata(AppliedOutcomeDefinitiveRefusalFlow.Message, MessageState.Acked, 1, 3, 1,
            null, null, LeaseVersion: 1);
        var healthy = new MessageMetadata(AppliedOutcomeDefinitiveRefusalFlow.Healthy,
            completed ? MessageState.Acked : MessageState.Ready, completed ? 1 : 0, completed ? 3 : 1, 2,
            null, null, LeaseVersion: completed ? 1 : 0);
        var message = new EnqueueMessage(lane.Queue, healthy.Id, AppliedOutcomeDefinitiveRefusalFlow.Payload,
            AppliedOutcomeDefinitiveRefusalFlow.Headers);
        var body = completed ? null : new MessageBody(healthy.Id, message.PayloadJson, message.HeadersJson,
            message.OrderingKey, JsonData.Fingerprint(message));
        await Assert.That(db.InspectMessage("root", lane, original.Id)).IsEqualTo(new MessageInspection(original, null, null));
        await Assert.That(db.InspectMessage("root", lane, healthy.Id)).IsEqualTo(new MessageInspection(healthy, body?.PayloadJson, body?.HeadersJson));
        await Assert.That(store.Read(view => view.GetRecord<MessageMetadata>(KeySpace.Partition("message-meta", lane.Partition, lane.Queue, original.Id)))).IsEqualTo(original);
        await Assert.That(store.Read(view => view.GetRecord<MessageMetadata>(KeySpace.Partition("message-meta", lane.Partition, lane.Queue, healthy.Id)))).IsEqualTo(healthy);
        await Assert.That(store.Read(view => view.GetRecord<MessageBody>(KeySpace.Partition("message-body", lane.Partition, lane.Queue, original.Id)))).IsNull();
        await Assert.That(store.Read(view => view.GetRecord<MessageBody>(KeySpace.Partition("message-body", lane.Partition, lane.Queue, healthy.Id)))).IsEqualTo(body);
        var bytes = body is null ? 0 : OrleansSerialization.Serialize(body).LongLength;
        await Assert.That(store.Read(view => view.GetRecord<QueueCounters>(KeySpace.Partition("queue-counters", lane.Partition, lane.Queue))))
            .IsEqualTo(new QueueCounters(completed ? 0 : 1, bytes, 0, 0, 2));
        var ready = store.Read(view => view.Scan(KeySpace.Partition("ready", lane.Partition, lane.Queue), 3));
        await Assert.That(ready.HasMore).IsFalse();
        await Assert.That(ready.Records.Length).IsEqualTo(completed ? 0 : 1);
        if (!completed)
        {
            await Assert.That(ready.Records[0].Key.Span.SequenceEqual(KeySpace.Partition("ready", lane.Partition, lane.Queue, 2L, healthy.Id))).IsTrue();
            await Assert.That(OrleansSerialization.Deserialize<string>(ready.Records[0].Value.Span)).IsEqualTo(healthy.Id);
        }
        foreach (var family in new[] { "lease", "scheduled" })
        {
            var page = store.Read(view => view.Scan(KeySpace.Partition(family, lane.Partition, lane.Queue), 3));
            await Assert.That(page.HasMore).IsFalse();
            await Assert.That(page.Records).IsEmpty();
        }
    }

    private static void Put(ZoneTreeStore store, byte[] key, byte[] bytes)
        => store.Commit((tx, _) =>
        {
            tx.Put(key, bytes);
            return true;
        });
}
