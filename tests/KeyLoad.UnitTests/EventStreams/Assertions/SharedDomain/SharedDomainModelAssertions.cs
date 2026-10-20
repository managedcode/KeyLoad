using KeyLoad.RecoveryTests.Features.EventStreams;
using static KeyLoad.RecoveryTests.Features.EventStreams.SharedDomainProtocol;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class SharedDomainModelAssertions
{
    private const long NoRecords = 0L;
    private const int ProducerMutations = 3;

    internal static async Task AllAsync(DatabaseEngine database, SharedDomainState state, CancellationToken token)
    {
        foreach (var scope in state.Scopes)
        {
            var processed = state.Processed && scope.Partition.TransactionDomainId == FirstDomain;
            var healthy = state.Healthy.Contains(scope.Partition.AtomicPartitionId);
            if (scope.Documents is { } documents)
            {
                await DocumentAsync(database, scope, documents, Item, true, token);
                await DocumentAsync(database, scope, documents, Processed, processed, token);
                await DocumentAsync(database, scope, documents, Healthy, healthy, token);
                await DocumentAsync(database, scope, documents, Refused, false, token);
            }
            if (scope.Streams is { } streams)
            {
                await StreamAsync(database, state, scope, streams, Item, processed ? SecondRevision : FirstRevision,
                    FirstRevision, token);
                await StreamAsync(database, state, scope, streams, Healthy, healthy ? FirstRevision : NoRevision,
                    processed ? AckVersion : SecondRevision, token);
                await StreamAsync(database, state, scope, streams, Refused, NoRevision, NoRevision, token);
            }
            if (scope.Queue is not null)
            {
                await SharedDomainQueueAssertions.RequireAsync(database, scope, processed, healthy);
            }
            var outbox = database.GetOutboxStatus(Root, scope.Partition);
            var expected = scope.Resources.Length + (processed ? ProducerMutations : NoRecords)
                + (healthy ? scope.Resources.Length : NoRecords);
            await Assert.That(outbox.Head.Tail).IsEqualTo(expected);
            await Assert.That(outbox.Head.StoredRecords).IsEqualTo(expected);
            await Assert.That(outbox.Head.FirstAvailable).IsEqualTo(FirstRevision);
            await Assert.That(outbox.Head.StoredBytes).IsGreaterThan(NoRecords);
            await Assert.That(outbox.Consumers).IsEmpty();
        }
    }

    private static async Task DocumentAsync(DatabaseEngine database, SharedDomainScope scope, string collection,
        string id, bool present, CancellationToken token)
    {
        var reference = new EntityRef(scope.Partition, collection, id);
        var actual = database.GetDocument(Root, reference, cancellationToken: token);
        await SharedDomainValueAssertions.EqualAsync(actual, present ? new DocumentResult(reference, FirstRevision, scope.Payload, false, []) : null);
    }

    private static async Task StreamAsync(DatabaseEngine database, SharedDomainState state, SharedDomainScope scope,
        string streams, string id, long tail, long sequence, CancellationToken token)
    {
        var stream = new StreamRef(scope.Partition, streams, id);
        var actual = database.ReadStream(Root, stream, cancellationToken: token);
        await Assert.That(actual.Stream).IsEqualTo(stream);
        await Assert.That(actual.Head).IsEqualTo(new StreamHead(tail, FirstRevision, Generation));
        await Assert.That(actual.Events.Length).IsEqualTo(checked((int)tail));
        await Assert.That(actual.HasMore).IsFalse();
        await Assert.That(actual.Cursor).IsNull();
        await Assert.That(actual.CutPosition).IsEqualTo(database.Store.Position);
        await Assert.That(actual.SnapshotCutPosition).IsEqualTo(database.Store.Position);
        List<EventRecord> expected = [];
        if (tail > NoRevision)
        {
            var time = state.Recorded[scope.Partition.AtomicPartitionId + IdentitySeparator + id];
            expected.Add(new(stream, FirstRevision, sequence, new(id, EventType, scope.Payload, Headers), time));
        }
        if (tail == SecondRevision)
        {
            var time = state.Recorded[scope.Partition.AtomicPartitionId + IdentitySeparator + Processed];
            expected.Add(new(stream, SecondRevision, SecondRevision, new(Processed, EventType, scope.Payload, Headers), time));
        }
        await SharedDomainValueAssertions.EqualAsync(actual.Events.ToArray(), expected.ToArray());
    }
}
