using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueBodyFailureAssertions
{
    private const string RootPrincipalId = "root";
    private const string CountersFamily = "queue-counters";

    internal static async Task RejectWithoutEffects(TestDatabase db, QueueLaneRef lane, Guid operationId,
        byte[] bodyKey, byte[] readyKey, byte[]? body)
    {
        var position = db.Store.Position;
        var applied = db.Database.LastApplied;
        var ready = db.Store.Read(view => view.ReadOwnedValue(readyKey)!);
        var countersKey = KeySpace.Partition(CountersFamily, db.Partition, lane.Queue);
        var counters = db.Store.Read(view => view.GetRecord<QueueCounters>(countersKey));
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => db.Submit(OperationKind.Receive,
            new ReceiveRequest(operationId, lane), id: operationId));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(db.Store.Position).IsEqualTo(position);
        await Assert.That(db.Database.LastApplied).IsEqualTo(applied);
        var actualBody = db.Store.Read(view => view.ReadOwnedValue(bodyKey));
        await Assert.That(actualBody is null ? body is null : body is not null && actualBody.AsSpan().SequenceEqual(body)).IsTrue();
        await Assert.That(db.Store.Read(view => view.ReadOwnedValue(readyKey)!).AsSpan().SequenceEqual(ready)).IsTrue();
        await Assert.That(db.Store.Read(view => view.GetRecord<QueueCounters>(countersKey))).IsEqualTo(counters);
        await Assert.That(db.Store.Read(view => view.ReadOwnedValue(KeySpace.Outcome(RootPrincipalId, operationId)))).IsNull();
    }
}
