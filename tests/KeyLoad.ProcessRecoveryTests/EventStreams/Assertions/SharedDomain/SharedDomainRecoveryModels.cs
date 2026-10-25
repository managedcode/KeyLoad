using KeyLoad.Core;
using static KeyLoad.ProcessRecoveryTests.EventStreams.SharedDomainProtocol;

namespace KeyLoad.ProcessRecoveryTests.EventStreams;

internal static class SharedDomainRecoveryModels
{
    internal static async Task RequireAsync(DatabaseEngine database, SharedDomainRecoveryInput input, bool committed, bool healthy, CancellationToken token)
    {
        var lifetimes = await SharedDomainRecoveryReferences.CaptureAsync(input);
        var scopes = SharedDomainCorpus.Create(Tenant, Database);
        foreach (var scope in scopes)
        {
            var present = scope.Partition.TransactionDomainId != FirstDomain || committed;
            var continued = healthy && scope.Partition.TransactionDomainId is FirstDomain or SecondDomain;
            if (scope.Documents is { } documents)
            {
                await DocumentAsync(database, scope, documents, Item, present, lifetimes[documents], token);
                await DocumentAsync(database, scope, documents, Healthy, continued, lifetimes[documents], token);
            }
            if (scope.Streams is { } streams)
            {
                await StreamAsync(database, scope, streams, Item, present, FirstRevision, new DateTimeOffset(input.Original.Timestamp), token);
                await StreamAsync(database, scope, streams, Healthy, continued, SecondRevision, new DateTimeOffset(input.Original.Timestamp), token);
            }
            if (scope.Queue is not null)
            {
                await SharedDomainRecoveryQueue.RequireAsync(database, scope, present, continued);
            }
            var outbox = database.GetOutboxStatus(Root, scope.Partition);
            var count = (present ? scope.Resources.Length : NoRevision) + (continued ? scope.Resources.Length : NoRevision);
            await Assert.That(outbox.Head.Tail).IsEqualTo(count);
            await Assert.That(outbox.Head.StoredRecords).IsEqualTo(count);
            await Assert.That(outbox.Head.FirstAvailable).IsEqualTo(FirstRevision);
            await Assert.That(outbox.Consumers).IsEmpty();
            await Assert.That(outbox.Head.StoredBytes > NoRevision).IsEqualTo(present);
        }
    }

    private static async Task DocumentAsync(DatabaseEngine database, SharedDomainScope scope, string collection, string id,
        bool present, ResourceLifetime lifetime, CancellationToken token)
    {
        var reference = new EntityRef(scope.Partition, collection, id)
        {
            Lifetime = new(lifetime.Incarnation.Value)
        };
        await SharedDomainRecoveryValues.EqualAsync(database.GetDocument(Root, reference, cancellationToken: token),
            present ? new DocumentResult(reference, FirstRevision, scope.Payload, false, []) : null);
    }

    private static async Task StreamAsync(DatabaseEngine database, SharedDomainScope scope, string streams, string id,
        bool present, long sequence, DateTimeOffset time, CancellationToken token)
    {
        var stream = new StreamRef(scope.Partition, streams, id);
        var actual = database.ReadStream(Root, stream, cancellationToken: token);
        await Assert.That(actual.Stream).IsEqualTo(stream);
        await Assert.That(actual.Head).IsEqualTo(new StreamHead(present ? FirstRevision : NoRevision, FirstRevision, Generation));
        await Assert.That(actual.CutPosition).IsEqualTo(database.LastApplied);
        await Assert.That(actual.SnapshotCutPosition).IsEqualTo(database.LastApplied);
        await Assert.That(actual.HasMore).IsFalse();
        await Assert.That(actual.Cursor).IsNull();
        EventRecord[] expected = present ? [new(stream, FirstRevision, sequence, new(id, EventType, scope.Payload, Headers), time)] : [];
        await SharedDomainRecoveryValues.EqualAsync(actual.Events.ToArray(), expected);
    }
}
