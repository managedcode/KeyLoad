using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueEnqueueColdAssertions
{
    internal static string[] LaneBytes(ZoneTreeStore store, PartitionRef partition)
    {
        string[] families = [QueueEnqueueColdProtocol.BodyFamily, QueueEnqueueColdProtocol.MetadataFamily,
            QueueEnqueueColdProtocol.CountersFamily, QueueEnqueueColdProtocol.ReadyFamily, QueueEnqueueColdProtocol.ScheduledFamily];
        var prefixes = families.Select(family => Convert.ToHexString(KeySpace.Partition(family, partition, QueueEnqueueColdProtocol.Queue))).ToArray();
        return QueueWholeFlowStorage.Bytes(store).Where(row => prefixes.Any(prefix => row.StartsWith(prefix, StringComparison.Ordinal))).ToArray();
    }

    internal static async Task RequireReceiptAsync(CommitReceipt receipt, CommandRequest command, Guid incarnation)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(incarnation);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
        MutationReceipt[] expected = [new(QueueEnqueueColdProtocol.PutKind, QueueEnqueueColdProtocol.Collection,
            QueueEnqueueColdProtocol.Document, QueueEnqueueColdProtocol.InitialRevision),
            new(QueueEnqueueColdProtocol.EnqueueKind, QueueEnqueueColdProtocol.Queue, QueueEnqueueColdProtocol.Ready,
                QueueEnqueueColdProtocol.InitialRevision),
            new(QueueEnqueueColdProtocol.EnqueueKind, QueueEnqueueColdProtocol.Queue, QueueEnqueueColdProtocol.Scheduled,
                QueueEnqueueColdProtocol.InitialRevision)];
        await Assert.That(receipt.Mutations).IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    internal static async Task RequireProducerAsync(DatabaseEngine database, PartitionRef partition)
    {
        var reference = new EntityRef(partition, QueueEnqueueColdProtocol.Collection, QueueEnqueueColdProtocol.Document);
        var actual = database.GetDocument(QueueEnqueueColdProtocol.Root, reference);
        await Assert.That(actual).IsNotNull();
        await Assert.That(actual!.Reference).IsEqualTo(reference);
        await Assert.That(actual.Json).IsEqualTo(QueueEnqueueColdProtocol.ReadyJson);
        await Assert.That(actual.Revision).IsEqualTo(QueueEnqueueColdProtocol.InitialRevision);
        await Assert.That(actual.Redacted).IsFalse();
        await Assert.That(actual.RedactedFields).IsEmpty();
    }

    internal static async Task RequireInitialAsync(DatabaseEngine database, PartitionRef partition, DateTimeOffset scheduledAt)
    {
        await RequireMessageAsync(database, partition, QueueEnqueueColdProtocol.Ready, QueueEnqueueColdProtocol.ReadyJson,
            MessageState.Ready, QueueEnqueueColdProtocol.FirstReadySequence, null);
        await RequireMessageAsync(database, partition, QueueEnqueueColdProtocol.Scheduled, QueueEnqueueColdProtocol.ScheduledJson,
            MessageState.Scheduled, QueueEnqueueColdProtocol.ScheduledReadySequence, scheduledAt);
        await RequireCountersAsync(database, partition, QueueEnqueueColdProtocol.Ready, QueueEnqueueColdProtocol.Scheduled,
            QueueEnqueueColdProtocol.FirstReadySequence);
        var ready = database.Store.Read(view => view.GetRecord<string>(KeySpace.Partition(QueueEnqueueColdProtocol.ReadyFamily,
            partition, QueueEnqueueColdProtocol.Queue, QueueEnqueueColdProtocol.FirstReadySequence, QueueEnqueueColdProtocol.Ready)));
        var scheduled = database.Store.Read(view => view.GetRecord<string>(KeySpace.Partition(QueueEnqueueColdProtocol.ScheduledFamily,
            partition, QueueEnqueueColdProtocol.Queue, scheduledAt, QueueEnqueueColdProtocol.Scheduled)));
        await Assert.That(ready).IsEqualTo(QueueEnqueueColdProtocol.Ready);
        await Assert.That(scheduled).IsEqualTo(QueueEnqueueColdProtocol.Scheduled);
    }

    internal static async Task RequireHealthyAsync(DatabaseEngine database, PartitionRef partition, DateTimeOffset scheduledAt)
    {
        var lane = new QueueLaneRef(partition, QueueEnqueueColdProtocol.Queue);
        await Assert.That(database.InspectMessage(QueueEnqueueColdProtocol.Root, lane, QueueEnqueueColdProtocol.Ready)!.Metadata.State).IsEqualTo(MessageState.Acked);
        await RequireMessageAsync(database, partition, QueueEnqueueColdProtocol.Scheduled, QueueEnqueueColdProtocol.ScheduledJson,
            MessageState.Scheduled, QueueEnqueueColdProtocol.ScheduledReadySequence, scheduledAt);
        await RequireMessageAsync(database, partition, QueueEnqueueColdProtocol.Healthy, QueueEnqueueColdProtocol.HealthyJson,
            MessageState.Ready, QueueEnqueueColdProtocol.HealthyReadySequence, null, headers: QueueEnqueueColdProtocol.EmptyHeaders);
        await RequireCountersAsync(database, partition, QueueEnqueueColdProtocol.Scheduled, QueueEnqueueColdProtocol.Healthy,
            QueueEnqueueColdProtocol.HealthyReadySequence);
    }

    internal static async Task RequireRefusalAsync(DatabaseEngine database, CommandRequest command, string principal,
        ErrorCode code, string[] originalImage, CancellationToken token)
    {
        var actual = QueueEnqueueColdTrial.Apply(database, command, principal, token);
        await Assert.That(actual.Error).IsEqualTo(code);
        await Assert.That(actual.NativeValue).IsNull();
        await Assert.That(LaneBytes((ZoneTreeStore)database.Store, command.Partition)).IsEquivalentTo(originalImage, CollectionOrdering.Matching);
        await Assert.That(database.GetDocument(QueueEnqueueColdProtocol.Root,
            new(command.Partition, QueueEnqueueColdProtocol.Collection, QueueEnqueueColdProtocol.RefusedDocument), cancellationToken: token)).IsNull();
        var outcome = database.Store.Read(view => view.GetRecord<StoredOutcome>(KeySpace.PartitionOutcome(command.Partition, principal, command.CommandId)));
        await Assert.That(outcome).IsNotNull();
        await Assert.That(outcome!.Result.Error).IsEqualTo(code);
        await Assert.That(outcome.Result.Json).IsNull();
    }

    private static async Task RequireMessageAsync(DatabaseEngine database, PartitionRef partition, string id,
        string payload, MessageState state, long sequence, DateTimeOffset? notBefore, string headers = QueueEnqueueColdProtocol.Headers)
    {
        var actual = database.InspectMessage(QueueEnqueueColdProtocol.Root, new(partition, QueueEnqueueColdProtocol.Queue), id);
        await Assert.That(actual).IsNotNull();
        await Assert.That(actual!.PayloadJson).IsEqualTo(payload);
        await Assert.That(actual.HeadersJson).IsEqualTo(headers);
        await Assert.That(actual.Metadata).IsEqualTo(new MessageMetadata(id, state, QueueEnqueueColdProtocol.InitialAttempts,
            QueueEnqueueColdProtocol.InitialStateVersion, sequence, notBefore, null));
        var body = database.Store.Read(view => view.GetRecord<MessageBody>(KeySpace.Partition(QueueEnqueueColdProtocol.BodyFamily,
            partition, QueueEnqueueColdProtocol.Queue, id)));
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.Id).IsEqualTo(id);
        await Assert.That(body.PayloadJson).IsEqualTo(payload);
        await Assert.That(body.HeadersJson).IsEqualTo(headers);
        await Assert.That(body.OrderingKey).IsNull();
        await Assert.That(body.Fingerprint).IsEqualTo(JsonData.Fingerprint(new EnqueueMessage(QueueEnqueueColdProtocol.Queue,
            id, payload, headers, NotBefore: notBefore)));
    }

    private static async Task RequireCountersAsync(DatabaseEngine database, PartitionRef partition,
        string first, string second, long sequence)
    {
        var bytes = database.Store.Read(view => checked(view.ReadOwnedValue(KeySpace.Partition(QueueEnqueueColdProtocol.BodyFamily,
            partition, QueueEnqueueColdProtocol.Queue, first))!.LongLength + view.ReadOwnedValue(KeySpace.Partition(QueueEnqueueColdProtocol.BodyFamily,
            partition, QueueEnqueueColdProtocol.Queue, second))!.LongLength));
        var counters = database.Store.Read(view => view.GetRecord<QueueCounters>(KeySpace.Partition(QueueEnqueueColdProtocol.CountersFamily,
            partition, QueueEnqueueColdProtocol.Queue)));
        await Assert.That(counters).IsEqualTo(new QueueCounters(QueueEnqueueColdProtocol.MaximumStoredMessages,
            bytes, QueueEnqueueColdProtocol.NoInFlight, QueueEnqueueColdProtocol.NoInFlight, sequence));
    }
}
