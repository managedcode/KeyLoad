using KeyLoad.Core;
using KeyLoad.CrashHost;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class QueueClaimAckRecoveryAssertions
{
    internal static async Task ImageAsync(DatabaseEngine database, string id, MessageState state, Delivery? delivery, long sequence, long? counterSequence = null)
    {
        var leased = state == MessageState.Leased;
        var acked = state == MessageState.Acked;
        var metadata = new MessageMetadata(id, state, state == MessageState.Ready ? 0 : 1,
            acked ? 3 : leased ? 2 : 1, sequence, null, null,
            leased ? CrashFixtureValues.Principal : null, state == MessageState.Ready ? 0 : 1,
            leased ? delivery!.LeaseUntil : null);
        var expected = new MessageInspection(metadata, acked ? null : QueueClaimAckCrashProtocol.Payload,
            acked ? null : QueueClaimAckCrashProtocol.Headers);
        await EqualAsync(database.InspectMessage(CrashFixtureValues.Principal, QueueClaimAckCrashProtocol.Lane, id), expected);
        var enqueue = new EnqueueMessage(QueueClaimAckCrashProtocol.Queue, id, QueueClaimAckCrashProtocol.Payload,
            QueueClaimAckCrashProtocol.Headers);
        var body = new MessageBody(id, enqueue.PayloadJson, enqueue.HeadersJson, enqueue.OrderingKey, JsonData.Fingerprint(enqueue));
        var bytes = NativeSerialization.Serialize(body).LongLength;
        var lane = QueueClaimAckCrashProtocol.Lane;
        await Assert.That(database.Store.Read(view => view.GetRecord<MessageBody>(KeySpace.Partition("message-body", lane.Partition,
            lane.Queue, id)))).IsEqualTo(acked ? null : body);
        await Assert.That(database.Store.Read(view => view.GetRecord<QueueCounters>(KeySpace.Partition("queue-counters", lane.Partition,
            lane.Queue)))).IsEqualTo(new QueueCounters(acked ? 0 : 1, acked ? 0 : bytes, leased ? 1 : 0, leased ? bytes : 0, counterSequence ?? sequence));
        if (delivery is not null)
        {
            await Assert.That(database.Store.Read(view => view.GetRecord<string>(KeySpace.Partition("lease", lane.Partition,
                lane.Queue, delivery.LeaseUntil, id)))).IsEqualTo(leased ? id : null);
        }
        await Assert.That(database.Store.Read(view => view.GetRecord<string>(KeySpace.Partition("ready", lane.Partition,
            lane.Queue, sequence, id)))).IsEqualTo(state == MessageState.Ready ? id : null);
        await IndexAsync(database, "ready", state == MessageState.Ready
            ? KeySpace.Partition("ready", lane.Partition, lane.Queue, sequence, id) : null, id);
        await IndexAsync(database, "lease", leased
            ? KeySpace.Partition("lease", lane.Partition, lane.Queue, delivery!.LeaseUntil, id) : null, id);
    }

    private static async Task IndexAsync(DatabaseEngine database, string space, byte[]? expectedKey, string id)
    {
        var lane = QueueClaimAckCrashProtocol.Lane;
        var page = database.Store.Read(view => view.Scan(KeySpace.Partition(space, lane.Partition, lane.Queue), 1));
        await Assert.That(page.HasMore).IsFalse();
        string[] expected = expectedKey is null ? []
            : [Convert.ToHexString(expectedKey) + ":" + Convert.ToHexString(NativeSerialization.Serialize(id))];
        var actual = page.Records.Select(row => Convert.ToHexString(row.Key.Span) + ":"
            + Convert.ToHexString(row.Value.Span)).ToArray();
        await Assert.That(actual).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    internal static async Task DeliveryAsync(DatabaseEngine database, ReceiveResult result, Guid id,
        DateTimeOffset evaluatedAt, string message, CommitToken expectedToken)
    {
        await Assert.That(result.RequestId).IsEqualTo(id);
        await EqualAsync(result.Token, expectedToken);
        var actual = await Assert.That(result.Deliveries).HasSingleItem();
        await Assert.That(actual.Token).IsNotEqualTo(string.Empty);
        await EqualAsync(actual, new Delivery(message, QueueClaimAckCrashProtocol.Payload, QueueClaimAckCrashProtocol.Headers,
            actual.Token, 1, evaluatedAt.AddSeconds(QueueClaimAckCrashProtocol.LeaseSeconds), 1, 1));
        await EqualAsync(database.Verify<DeliveryClaims>(actual.Token), new DeliveryClaims(QueueClaimAckCrashProtocol.Lane,
            message, CrashFixtureValues.Principal, 1, 1, database.Store.Identity.Incarnation));
    }

    internal static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(NativeSerialization.Serialize(actual).AsSpan().SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();

    internal static string[] Capture(ZoneTreeStore store) => store.Read(view =>
    {
        var page = view.Scan([], QueueClaimAckCrashProtocol.ImageRecords);
        if (page.HasMore)
        {
            throw new InvalidOperationException("The complete claim/ACK image exceeded its admitted test bound.");
        }
        return page.Records.Select(row => Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span)).ToArray();
    });
}
