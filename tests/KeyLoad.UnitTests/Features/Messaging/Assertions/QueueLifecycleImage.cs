using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueLifecycleImage
{
    private static readonly string[] Families =
    [QueueLifecycleTestProtocol.ReadySpace, QueueLifecycleTestProtocol.ScheduledSpace, QueueLifecycleTestProtocol.LeaseSpace,
     QueueLifecycleTestProtocol.BodySpace, QueueLifecycleTestProtocol.MetadataSpace, QueueLifecycleTestProtocol.CounterSpace,
     QueueLifecycleTestProtocol.DeadLetterSpace, QueueLifecycleTestProtocol.PendingSpace, QueueLifecycleTestProtocol.ParkedSpace];

    internal static string[] Capture(ZoneTreeStore store, QueueLaneRef lane) => store.Read(view => Families.SelectMany(family =>
    {
        var page = view.Scan(KeySpace.Partition(family, lane.Partition, lane.Queue), QueueLifecycleTestProtocol.ImageRecords);
        if (page.HasMore)
        { throw new InvalidOperationException("The native lifecycle fixture image exceeded its bound."); }
        return page.Records.Select(row => Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span));
    }).ToArray());

    internal static async Task SameAsync(ZoneTreeStore store, QueueLaneRef lane, string[] expected)
        => await Assert.That(Capture(store, lane)).IsEquivalentTo(expected, CollectionOrdering.Matching);

    internal static async Task BodyAsync(DatabaseEngine database, QueueLifecycleTestState state, string id,
        MessageState expectedState, long version, long generation, long parkedSequence)
    {
        var actual = database.InspectMessage(QueueLifecycleTestProtocol.Administrator, state.Lane, id)!;
        var pending = id == QueueLifecycleTestProtocol.Pending;
        var healthy = id == QueueLifecycleTestProtocol.Healthy;
        var redriven = pending && generation == QueueLifecycleTestProtocol.Two;
        var sequence = Sequence(id, redriven, healthy, pending);
        var attempts = redriven && expectedState == MessageState.Ready ? QueueLifecycleTestProtocol.None : QueueLifecycleTestProtocol.One;
        var lease = redriven && expectedState == MessageState.Acked ? QueueLifecycleTestProtocol.Three
            : generation == QueueLifecycleTestProtocol.Two ? QueueLifecycleTestProtocol.Two : QueueLifecycleTestProtocol.One;
        var reason = expectedState is MessageState.PendingDeadLetter or MessageState.DeadLettered
            || id == QueueLifecycleTestProtocol.Parked && expectedState == MessageState.Cancelled ? "AttemptsExhausted" : null;
        var expected = new MessageMetadata(id, expectedState, attempts, version, sequence, null, null,
            LeaseVersion: lease, DeliveryGeneration: generation, SafeFailureCode: reason, ParkedSequence: parkedSequence);
        await Assert.That(JsonSerializer.Serialize(actual.Metadata, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));
        await Assert.That(actual.PayloadJson).IsEqualTo(expectedState is MessageState.Cancelled or MessageState.Acked
            ? null : QueueLifecycleTestProtocol.Payload);
        await Assert.That(actual.HeadersJson).IsEqualTo(expectedState is MessageState.Cancelled or MessageState.Acked
            ? null : QueueLifecycleTestProtocol.Headers);
        var body = database.Store.Read(view => view.GetRecord<MessageBody>(KeySpace.Partition(
            QueueLifecycleTestProtocol.BodySpace, state.Partition, state.Lane.Queue, id)));
        var literal = QueueLifecycleHealthy.Enqueue(id);
        var expectedBody = expectedState is MessageState.Cancelled or MessageState.Acked ? null
            : new MessageBody(id, literal.PayloadJson, literal.HeadersJson, literal.OrderingKey, JsonData.Fingerprint(literal));
        await Assert.That(body).IsEqualTo(expectedBody);
    }
    private static long Sequence(string id, bool redriven, bool healthy, bool pending)
    {
        if (redriven)
        { return QueueLifecycleTestProtocol.Four; }
        if (healthy)
        { return QueueLifecycleTestProtocol.Five; }
        if (pending)
        { return QueueLifecycleTestProtocol.Two; }
        return id == QueueLifecycleTestProtocol.Held ? QueueLifecycleTestProtocol.Three : QueueLifecycleTestProtocol.One;
    }
}
