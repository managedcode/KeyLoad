using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueEnqueueSequenceAssertions
{
    internal static async Task ResultAsync(DatabaseEngine owner, ZoneTreeStore store, QueueLaneRef lane,
        CommandRequest command, EnqueueMessage message, QueueCounters counters, Result<object> result,
        CommitToken expected, bool succeeds, string[] domain)
    {
        if (!succeeds)
        {
            await Assert.That(Errors.Code(result.Problem)).IsEqualTo(ErrorCode.ResourceExhausted);
            await Assert.That(result.Problem?.Detail).IsEqualTo(QueueEnqueueAuthorityProtocol.ExpectedSequenceDetail);

            await Assert.That(result.Value).IsNull();
            await QueueClaimVersionExhaustionAssertions.DomainAsync(store, lane, domain);
            await Assert.That(owner.GetDocument(QueueEnqueueColdProtocol.Root, new(lane.Partition, QueueEnqueueColdProtocol.Collection, QueueEnqueueColdProtocol.Document))).IsNull();
            await Assert.That(owner.InspectMessage(QueueEnqueueColdProtocol.Root, lane, message.MessageId)).IsNull();
            await Assert.That(store.Read(view => view.Scan(KeySpace.Partition(QueueEnqueueAuthorityProtocol.OrderFamily, lane.Partition, lane.Queue), QueueClaimVersionExhaustionProtocol.ImageRecords).Records)).IsEmpty();
            return;
        }
        await QueueStoredCapacityReceipts.ReceiptAsync(result, command.CommandId, expected,
            [new(QueueEnqueueColdProtocol.PutKind, QueueEnqueueColdProtocol.Collection, QueueEnqueueColdProtocol.Document, QueueEnqueueAuthorityProtocol.First), new(QueueEnqueueColdProtocol.EnqueueKind, lane.Queue, message.MessageId, QueueEnqueueAuthorityProtocol.First)]);
        await MessageAsync(owner, store, lane, message, new(message.MessageId, MessageState.Scheduled, QueueEnqueueAuthorityProtocol.Initial, QueueEnqueueAuthorityProtocol.First, QueueEnqueueAuthorityProtocol.Initial, message.NotBefore, null));
        var body = new MessageBody(message.MessageId, message.PayloadJson, message.HeadersJson, message.OrderingKey, JsonData.Fingerprint(message));
        await Assert.That(store.Read(view => view.GetRecord<QueueCounters>(KeySpace.Partition(QueueEnqueueColdProtocol.CountersFamily, lane.Partition, lane.Queue))))
            .IsEqualTo(counters with
            {
                StoredMessages = QueueEnqueueAuthorityProtocol.First,
                StoredBytes = OrleansSerialization.Serialize(body).LongLength
            });
        var document = owner.GetDocument(QueueEnqueueColdProtocol.Root, new(lane.Partition, QueueEnqueueColdProtocol.Collection, QueueEnqueueColdProtocol.Document))!;
        await Assert.That(document.Json).IsEqualTo(message.PayloadJson);
        await Assert.That(document.Revision).IsEqualTo(QueueEnqueueAuthorityProtocol.FirstLong);
        await Assert.That(document.Redacted).IsFalse();
        await Assert.That(document.RedactedFields).IsEmpty();
    }

    internal static async Task MessageAsync(DatabaseEngine owner, ZoneTreeStore store, QueueLaneRef lane,
        EnqueueMessage message, MessageMetadata metadata)
    {
        await Assert.That(owner.InspectMessage(QueueEnqueueColdProtocol.Root, lane, message.MessageId))
            .IsEqualTo(new MessageInspection(metadata, message.PayloadJson, message.HeadersJson));
        await Assert.That(store.Read(view => view.GetRecord<MessageBody>(KeySpace.Partition(QueueEnqueueColdProtocol.BodyFamily, lane.Partition, lane.Queue, message.MessageId))))
            .IsEqualTo(new MessageBody(message.MessageId, message.PayloadJson, message.HeadersJson, message.OrderingKey, JsonData.Fingerprint(message)));
        var scheduled = metadata.State == MessageState.Scheduled;
        var key = scheduled ? KeySpace.Partition(QueueEnqueueColdProtocol.ScheduledFamily, lane.Partition, lane.Queue, metadata.NotBefore!.Value, message.MessageId)
            : KeySpace.Partition(QueueEnqueueColdProtocol.ReadyFamily, lane.Partition, lane.Queue, metadata.ReadySequence, message.MessageId);
        await Assert.That(store.Read(view => view.GetRecord<string>(key))).IsEqualTo(message.MessageId);
        var opposite = store.Read(view => view.Scan(KeySpace.Partition(scheduled ? QueueEnqueueColdProtocol.ReadyFamily : QueueEnqueueColdProtocol.ScheduledFamily, lane.Partition, lane.Queue), QueueClaimVersionExhaustionProtocol.ImageRecords));
        await Assert.That(opposite.HasMore).IsFalse();
        await Assert.That(opposite.Records.Any(row => OrleansSerialization.Deserialize<string>(row.Value.Span) == message.MessageId)).IsFalse();
        await Assert.That(store.Read(view => view.Scan(KeySpace.Partition(QueueEnqueueAuthorityProtocol.LeaseFamily, lane.Partition, lane.Queue), QueueClaimVersionExhaustionProtocol.ImageRecords).Records)).IsEmpty();
        var orders = store.Read(view => view.Scan(KeySpace.Partition(QueueEnqueueAuthorityProtocol.OrderFamily, lane.Partition, lane.Queue), QueueClaimVersionExhaustionProtocol.ImageRecords));
        await Assert.That(orders.HasMore).IsFalse();
        if (metadata.ActiveOrderSequence == QueueEnqueueAuthorityProtocol.Initial)
        {
            await Assert.That(orders.Records).IsEmpty();
        }
        else
        {
            var row = await Assert.That(orders.Records).HasSingleItem();
            await Assert.That(row.Key.ToArray()).IsEquivalentTo(KeySpace.Partition(QueueEnqueueAuthorityProtocol.OrderFamily, lane.Partition, lane.Queue,
                message.OrderingKey, metadata.ActiveOrderSequence, message.MessageId), CollectionOrdering.Matching);
            await Assert.That(OrleansSerialization.Deserialize<QueueOrderReference>(row.Value.Span))
                .IsEqualTo(new QueueOrderReference(message.MessageId, QueueEnqueueAuthorityProtocol.First, QueueEnqueueAuthorityProtocol.First));
        }
    }
}
