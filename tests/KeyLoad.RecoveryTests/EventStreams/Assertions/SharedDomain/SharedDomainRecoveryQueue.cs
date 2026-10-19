using KeyLoad.Core;
using KeyLoad.Storage;
using static KeyLoad.RecoveryTests.Features.EventStreams.SharedDomainProtocol;

namespace KeyLoad.RecoveryTests.Features.EventStreams;

internal static class SharedDomainRecoveryQueue
{
    private const string MetadataSpace = "message-meta";
    private const string BodySpace = "message-body";
    private const string ReadySpace = "ready";
    private const string CountersSpace = "queue-counters";
    private const string LeaseSpace = "lease";
    private const string ScheduledSpace = "scheduled";
    private const string DeadLetterSpace = "dead-letter";
    private const string PendingSpace = "queue-pending-dead-letter";
    private const string ParkedSpace = "queue-dead-letter-order";
    private const string OrderSpace = "queue-order";
    private const string InboxSpace = "inbox";
    private const int NoAttempts = 0;
    private const int OriginalAttempt = 1;

    internal static async Task RequireAsync(DatabaseEngine database, SharedDomainScope scope, bool present, bool healthy)
    {
        var lane = new QueueLaneRef(scope.Partition, scope.Queue!);
        var acknowledged = scope.Partition.TransactionDomainId == SecondDomain;
        List<MessageMetadata> records = [];
        if (present)
        {
            records.Add(new(Item, acknowledged ? MessageState.Acked : MessageState.Ready,
                acknowledged ? OriginalAttempt : NoAttempts, acknowledged ? AckVersion : FirstRevision, FirstRevision,
                null, null, LeaseVersion: acknowledged ? FirstRevision : NoRevision));
        }
        if (healthy)
        {
            records.Add(new(Healthy, MessageState.Ready, NoAttempts, FirstRevision, SecondRevision, null, null));
        }
        var bytes = NoRevision;
        foreach (var metadata in records)
        {
            bytes += await MessageAsync(database, scope, lane, metadata);
        }
        await Assert.That(Page(database.Store, MetadataSpace, lane).Records.Length).IsEqualTo(records.Count);
        var retained = records.Where(record => record.State == MessageState.Ready).ToArray();
        await Assert.That(Page(database.Store, BodySpace, lane).Records.Length).IsEqualTo(retained.Length);
        await Assert.That(Page(database.Store, ReadySpace, lane).Records.Length).IsEqualTo(retained.Length);
        foreach (var record in retained)
        {
            var key = KeySpace.Partition(ReadySpace, lane.Partition, lane.Queue, record.ReadySequence, record.Id);
            await SharedDomainRecoveryValues.EqualAsync(database.Store.Read(view => view.GetRecord<string>(key)), record.Id);
        }
        foreach (var family in new[] { LeaseSpace, ScheduledSpace, DeadLetterSpace, PendingSpace, ParkedSpace, OrderSpace, InboxSpace })
        {
            await Assert.That(Page(database.Store, family, lane).Records).IsEmpty();
        }
        var counterKey = KeySpace.Partition(CountersSpace, lane.Partition, lane.Queue);
        await SharedDomainRecoveryValues.EqualAsync(database.Store.Read(view => view.GetRecord<QueueCounters>(counterKey)),
            present ? new QueueCounters(retained.Length, bytes, NoRevision, NoRevision, healthy ? SecondRevision : FirstRevision) : null);
    }

    private static async Task<long> MessageAsync(DatabaseEngine database, SharedDomainScope scope, QueueLaneRef lane, MessageMetadata metadata)
    {
        var retained = metadata.State == MessageState.Ready;
        await SharedDomainRecoveryValues.EqualAsync(database.InspectMessage(Root, lane, metadata.Id),
            new MessageInspection(metadata, retained ? scope.Payload : null, retained ? Headers : null));
        var key = KeySpace.Partition(BodySpace, lane.Partition, lane.Queue, metadata.Id);
        var body = database.Store.Read(view => view.ReadOwnedValue(key));
        if (!retained)
        {
            await Assert.That(body).IsNull();
            return NoRevision;
        }
        await Assert.That(body).IsNotNull();
        await SharedDomainRecoveryValues.EqualAsync(OrleansSerialization.Deserialize<MessageBody>(body!),
            new MessageBody(metadata.Id, scope.Payload, Headers, null, JsonData.Fingerprint(new EnqueueMessage(lane.Queue, metadata.Id, scope.Payload, Headers))));
        return body!.LongLength;
    }

    private static ScanPage Page(IAtomicStore store, string family, QueueLaneRef lane) => store.Read(view =>
    {
        var page = view.Scan(KeySpace.Partition(family, lane.Partition, lane.Queue), ImageBound);
        if (page.HasMore)
        {
            throw new InvalidOperationException(ImageLimit);
        }
        return page;
    });
}
