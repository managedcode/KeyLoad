using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueLiveInspectionAuthorityAssertions
{
    internal const string PositiveQueue = "inspect-positive-retained";

    internal static async Task<(CommandRequest Producer, Result<object> Result)> SeedPositiveAsync(DatabaseEngine owner,
        ZoneTreeStore store, QueueLeaseAuthorityState state)
    {
        var producer = new CommandRequest(Guid.NewGuid(), state.Lane.Partition, [
            new EnqueueMessage(PositiveQueue, "ready", QueueLeaseAuthorityProtocol.Payload, QueueLeaseAuthorityProtocol.Headers),
            new EnqueueMessage(PositiveQueue, "scheduled", QueueLeaseAuthorityProtocol.Payload,
                QueueLeaseAuthorityProtocol.Headers, NotBefore: state.Start.AddHours(1))]);
        var result = QueueWholeFlowStorage.Apply(owner, OperationKind.Batch, producer, producer.CommandId, state.Start);
        result.Get<CommitReceipt>();
        var positive = (producer, result);
        await PositiveAsync(owner, store, state, positive);
        return positive;
    }

    internal static async Task PositiveAsync(DatabaseEngine owner, ZoneTreeStore store, QueueLeaseAuthorityState state,
        (CommandRequest Producer, Result<object> Result) positive)
    {
        var before = QueuePendingAuthorityHistory.Capture(store);
        var lane = new QueueLaneRef(state.Lane.Partition, PositiveQueue);
        long bytes = 0;
        foreach (var message in positive.Producer.Mutations.Cast<EnqueueMessage>())
        {
            var scheduled = message.NotBefore is not null;
            var metadata = new MessageMetadata(message.MessageId, scheduled ? MessageState.Scheduled : MessageState.Ready,
                0, 1, scheduled ? 0 : 1, message.NotBefore, null);
            var inspection = owner.InspectMessage(QueueLeaseAuthorityProtocol.Root, lane, message.MessageId)!;
            await Assert.That(inspection.Metadata).IsEqualTo(metadata);
            await Assert.That(inspection.PayloadJson).IsEqualTo(message.PayloadJson);
            await Assert.That(inspection.HeadersJson).IsEqualTo(message.HeadersJson);
            var body = new MessageBody(message.MessageId, message.PayloadJson, message.HeadersJson, null, JsonData.Fingerprint(message));
            var key = KeySpace.Partition("message-body", lane.Partition, lane.Queue, message.MessageId);
            await Assert.That(store.Read(view => view.ReadOwnedValue(key))!.SequenceEqual(OrleansSerialization.Serialize(body))).IsTrue();
            bytes += OrleansSerialization.Serialize(body).LongLength;
            var family = scheduled ? "scheduled" : "ready";
            object deadline = scheduled ? message.NotBefore!.Value : 1L;
            await Assert.That(store.Read(view => view.GetRecord<string>(KeySpace.Partition(family, lane.Partition, lane.Queue,
                deadline, message.MessageId)))).IsEqualTo(message.MessageId);
        }
        await Assert.That(QueueLeaseAuthoritySetup.Counters(store, lane)).IsEqualTo(new QueueCounters(2, bytes, 0, 0, 1));
        var replay = QueueWholeFlowStorage.Apply(owner, OperationKind.Batch, positive.Producer,
            positive.Producer.CommandId, state.Start);
        await Assert.That(OrleansSerialization.Serialize(replay).SequenceEqual(OrleansSerialization.Serialize(positive.Result))).IsTrue();
        await QueuePendingAuthorityHistory.SameAsync(store, before);
    }

    internal static async Task OneRowAsync(QueuePendingAuthorityCut original, QueuePendingAuthorityCut damaged,
        byte[] key, byte[]? replacement)
    {
        await Assert.That(damaged.Position).IsEqualTo(original.Position + 1);
        await Assert.That(damaged.Identity.SequenceEqual(original.Identity)).IsTrue();
        await Assert.That(damaged.Rows.Length).IsEqualTo(original.Rows.Length - (replacement is null ? 1 : 0));
        var prefix = Convert.ToHexString(key) + ":";
        string[] additions = replacement is null ? [] : [prefix + Convert.ToHexString(replacement)];
        await Assert.That(damaged.Rows.Except(original.Rows)).IsEquivalentTo(additions, CollectionOrdering.Matching);
        await Assert.That(original.Rows.Except(damaged.Rows)).IsEquivalentTo(
            [original.Rows.Single(row => row.StartsWith(prefix, StringComparison.Ordinal))], CollectionOrdering.Matching);
    }

    internal static async Task LeasedAsync(DatabaseEngine owner, ZoneTreeStore store, QueueLeaseAuthorityState state)
    {
        var before = QueuePendingAuthorityHistory.Capture(store);
        var inspection = owner.InspectMessage(QueueLeaseAuthorityProtocol.Root, state.Lane, state.Message.MessageId)!;
        await Assert.That(inspection.Metadata).IsEqualTo(state.Metadata);
        await Assert.That(inspection.PayloadJson).IsEqualTo(QueueLeaseAuthorityProtocol.Payload);
        await Assert.That(inspection.HeadersJson).IsEqualTo(QueueLeaseAuthorityProtocol.Headers);
        await Assert.That(store.Read(view => view.GetRecord<MessageBody>(state.FaultKey))).IsEqualTo(
            new MessageBody(state.Message.MessageId, QueueLeaseAuthorityProtocol.Payload, QueueLeaseAuthorityProtocol.Headers,
                null, JsonData.Fingerprint(state.Message)));
        await Assert.That(QueueLeaseAuthoritySetup.Counters(store, state.Lane)).IsEqualTo(state.Counters);
        await QueueLeaseAuthorityIndices.RequireAsync(store, state, state.Metadata);
        await Assert.That(owner.GetDocument(QueueLeaseAuthorityProtocol.Root,
            new(state.Lane.Partition, QueueLeaseAuthorityProtocol.Collection, QueueLeaseAuthorityProtocol.Document))).IsNull();
        var inbox = store.Read(view => view.Scan(KeySpace.Partition("inbox", state.Lane.Partition, state.Lane.Queue), 4));
        await Assert.That(inbox.HasMore).IsFalse();
        await Assert.That(inbox.Records).IsEmpty();
        await QueuePendingAuthorityHistory.SameAsync(store, before);
    }

    internal static async Task RefuseAsync(DatabaseEngine owner, ZoneTreeStore store, QueueLeaseAuthorityState state,
        QueuePendingAuthorityCut damaged)
    {
        await Assert.That(Outcome(store, state, state.Original.CommandId)).IsNull();
        var error = Assert.ThrowsExactly<KeyLoadException>(() => owner.InspectMessage(QueueLeaseAuthorityProtocol.Root,
            state.Lane, state.Message.MessageId));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(error.Message).IsEqualTo("The queue lifecycle body is unavailable.");
        await Assert.That(Outcome(store, state, state.Original.CommandId)).IsNull();
        await QueuePendingAuthorityHistory.SameAsync(store, damaged);
    }

    internal static async Task ReplayAsync(DatabaseEngine owner, ZoneTreeStore store, QueueLeaseAuthorityState state,
        Result<object> processed)
    {
        var before = QueuePendingAuthorityHistory.Capture(store);
        foreach (var (operation, result) in new[] { (state.Original, processed), (state.Claim, state.Claimed) })
        {
            var saved = Outcome(store, state, operation.CommandId)!;
            await Assert.That(saved.Incarnation).IsEqualTo(store.Identity.Incarnation);
            await Assert.That(OrleansSerialization.Serialize(saved.Result).SequenceEqual(OrleansSerialization.Serialize(result))).IsTrue();
            var replay = owner.Apply(operation);
            if (operation.Value!.Kind == OperationKind.Receive)
            {
                await Assert.That(Errors.Code(replay.Problem)).IsEqualTo(ErrorCode.StaleLease);

                await Assert.That(replay.Value).IsNull();
            }
            else
            {
                await Assert.That(OrleansSerialization.Serialize(replay).SequenceEqual(OrleansSerialization.Serialize(result))).IsTrue();
            }
        }
        var produced = QueueWholeFlowStorage.Apply(owner, OperationKind.Batch, state.Producer,
            state.Producer.CommandId, state.Start);
        await Assert.That(OrleansSerialization.Serialize(produced).SequenceEqual(OrleansSerialization.Serialize(state.Produced))).IsTrue();
        var producerOutcome = Outcome(store, state, state.Producer.CommandId)!;
        await Assert.That(OrleansSerialization.Serialize(producerOutcome.Result).SequenceEqual(OrleansSerialization.Serialize(state.Produced))).IsTrue();
        await QueuePendingAuthorityHistory.SameAsync(store, before);
    }

    internal static async Task PositiveRefuseAsync(DatabaseEngine owner, ZoneTreeStore store, QueueLeaseAuthorityState state,
        (CommandRequest Producer, Result<object> Result) positive, string target, QueuePendingAuthorityCut damaged)
    {
        await Assert.That(Outcome(store, state, state.Original.CommandId)).IsNull();
        var lane = new QueueLaneRef(state.Lane.Partition, PositiveQueue);
        var error = Assert.ThrowsExactly<KeyLoadException>(() => owner.InspectMessage(QueueLeaseAuthorityProtocol.Root, lane, target));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(error.Message).IsEqualTo("The queue lifecycle body is unavailable.");
        await Assert.That(Outcome(store, state, state.Original.CommandId)).IsNull();
        await QueuePendingAuthorityHistory.SameAsync(store, damaged);
        await LeasedAsync(owner, store, state);
        var sibling = positive.Producer.Mutations.Cast<EnqueueMessage>().Single(message => message.MessageId != target);
        var metadata = new MessageMetadata(sibling.MessageId, sibling.NotBefore is null ? MessageState.Ready : MessageState.Scheduled,
            0, 1, sibling.NotBefore is null ? 1 : 0, sibling.NotBefore, null);
        var inspection = owner.InspectMessage(QueueLeaseAuthorityProtocol.Root, lane, sibling.MessageId)!;
        await Assert.That(inspection.Metadata).IsEqualTo(metadata);
        await Assert.That(inspection.PayloadJson).IsEqualTo(sibling.PayloadJson);
        await Assert.That(inspection.HeadersJson).IsEqualTo(sibling.HeadersJson);
        var body = new MessageBody(sibling.MessageId, sibling.PayloadJson, sibling.HeadersJson, null, JsonData.Fingerprint(sibling));
        await Assert.That(store.Read(view => view.GetRecord<MessageBody>(KeySpace.Partition("message-body", lane.Partition,
            lane.Queue, sibling.MessageId)))).IsEqualTo(body);
        var replay = QueueWholeFlowStorage.Apply(owner, OperationKind.Batch, positive.Producer,
            positive.Producer.CommandId, state.Start);
        await Assert.That(OrleansSerialization.Serialize(replay).SequenceEqual(OrleansSerialization.Serialize(positive.Result))).IsTrue();
        await QueuePendingAuthorityHistory.SameAsync(store, damaged);
    }

    private static StoredOutcome? Outcome(ZoneTreeStore store, QueueLeaseAuthorityState state, Guid id)
        => store.Read(view => view.GetRecord<StoredOutcome>(KeySpace.PartitionOutcome(state.Lane.Partition, QueueLeaseAuthorityProtocol.Root, id)));
}
