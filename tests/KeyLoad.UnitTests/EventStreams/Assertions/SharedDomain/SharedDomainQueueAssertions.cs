using KeyLoad.Core;
using KeyLoad.RecoveryTests.Features.EventStreams;
using KeyLoad.Storage;
using static KeyLoad.RecoveryTests.Features.EventStreams.SharedDomainProtocol;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class SharedDomainQueueAssertions
{
    private const string Metadata = "message-meta";
    private const string Body = "message-body";
    private const string Ready = "ready";
    private const string Lease = "lease";
    private const string Scheduled = "scheduled";
    private const string DeadLetter = "dead-letter";
    private const string Pending = "queue-pending-dead-letter";
    private const string Parked = "queue-dead-letter-order";
    private const string Order = "queue-order";
    private const string Counters = "queue-counters";
    private const int NoAttempts = 0;
    private const int FirstAttempt = 1;

    internal static async Task RequireAsync(DatabaseEngine database, SharedDomainScope scope, bool processed, bool healthy)
    {
        var lane = new QueueLaneRef(scope.Partition, scope.Queue!);
        List<MessageMetadata> expected = [new(Item, processed ? MessageState.Acked : MessageState.Ready,
            processed ? FirstAttempt : NoAttempts, processed ? AckVersion : FirstRevision, FirstRevision,
            null, null, LeaseVersion: processed ? FirstRevision : NoRevision)];
        if (processed)
        {
            expected.Add(new(Processed, MessageState.Ready, NoAttempts, FirstRevision, SecondRevision, null, null));
        }
        if (healthy)
        {
            expected.Add(new(Healthy, MessageState.Ready, NoAttempts, FirstRevision, processed ? AckVersion : SecondRevision, null, null));
        }
        var bytes = NoRevision;
        foreach (var metadata in expected)
        {
            bytes += await MessageAsync(database, scope, lane, metadata);
        }
        await Assert.That(Page(database.Store, Metadata, lane).Records.Length).IsEqualTo(expected.Count);
        var retained = expected.Where(metadata => metadata.State == MessageState.Ready).ToArray();
        await Assert.That(Page(database.Store, Body, lane).Records.Length).IsEqualTo(retained.Length);
        await Assert.That(Page(database.Store, Ready, lane).Records.Length).IsEqualTo(retained.Length);
        foreach (var metadata in retained)
        {
            var key = KeySpace.Partition(Ready, lane.Partition, lane.Queue, metadata.ReadySequence, metadata.Id);
            await SharedDomainValueAssertions.EqualAsync(database.Store.Read(view => view.GetRecord<string>(key)), metadata.Id);
        }
        foreach (var family in new[] { Lease, Scheduled, DeadLetter, Pending, Parked, Order })
        {
            await Assert.That(Page(database.Store, family, lane).Records).IsEmpty();
        }
        var keyCounters = KeySpace.Partition(Counters, lane.Partition, lane.Queue);
        await SharedDomainValueAssertions.EqualAsync(database.Store.Read(view => view.GetRecord<QueueCounters>(keyCounters)),
            new QueueCounters(retained.Length, bytes, NoRevision, NoRevision, expected.Count));
        await Assert.That(database.InspectMessage(Root, lane, Refused)).IsNull();
    }

    private static async Task<long> MessageAsync(DatabaseEngine database, SharedDomainScope scope, QueueLaneRef lane, MessageMetadata metadata)
    {
        var retained = metadata.State == MessageState.Ready;
        await SharedDomainValueAssertions.EqualAsync(database.InspectMessage(Root, lane, metadata.Id),
            new MessageInspection(metadata, retained ? scope.Payload : null, retained ? Headers : null));
        var key = KeySpace.Partition(Body, lane.Partition, lane.Queue, metadata.Id);
        var body = database.Store.Read(view => view.ReadOwnedValue(key));
        if (!retained)
        {
            await Assert.That(body).IsNull();
            return NoRevision;
        }
        await Assert.That(body).IsNotNull();
        var input = new EnqueueMessage(lane.Queue, metadata.Id, scope.Payload, Headers);
        await SharedDomainValueAssertions.EqualAsync(NativeSerialization.Deserialize<MessageBody>(body!),
            new MessageBody(metadata.Id, scope.Payload, Headers, null, JsonData.Fingerprint(input)));
        var metadataKey = KeySpace.Partition(Metadata, lane.Partition, lane.Queue, metadata.Id);
        await SharedDomainValueAssertions.EqualAsync(database.Store.Read(view => view.GetRecord<MessageMetadata>(metadataKey)), metadata);
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
