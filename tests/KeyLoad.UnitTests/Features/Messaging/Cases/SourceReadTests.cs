using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class SourceReadTests
{
    private const string TopicName = "source-read-topic";
    private const string OtherTopicName = "different-topic";
    private const string StreamSetName = "source-read-streams";
    private const string StreamId = "stream-1";
    private const string ReaderId = "source-reader";
    private const string EventType = "Created";
    private const string SecretPath = "/secret";
    private const string PrivateValue = "private-source-value";
    private const string PublicValue = "public-source-value";
    private const string ProtectedPayload = "{\"secret\":\"" + PrivateValue + "\",\"public\":\"" + PublicValue + "\"}";
    private const string TopicEventSpace = "topic-event";
    private const string InvalidJson = "{";
    private const string StreamEventSpace = "event";

    [Test]
    public async Task AcMp005TopicAndStreamReadsPreserveOrderingCursorAndPolicyProjection()
    {
        using var database = new TestDatabase();
        ConfigureSources(database);
        PublishTopic(database, [new("topic-a", EventType, ProtectedPayload), new("topic-b", EventType, ProtectedPayload)]);
        AppendStream(database, [new("stream-a", EventType, ProtectedPayload), new("stream-b", EventType, ProtectedPayload)]);

        var topic = new EventSourceRef(database.Partition, TopicName, EventSourceKind.Topic);
        var first = database.Database.ReadEventSource(ReaderId, new(topic, Limit: 1));
        var next = database.Database.ReadEventSource(ReaderId, new(topic, Cursor: first.Cursor));
        var stream = new EventSourceRef(database.Partition, StreamSetName, EventSourceKind.Stream, StreamId);
        var streamPage = database.Database.ReadEventSource(ReaderId, new(stream));

        await Assert.That(first.Events.Select(record => record.Position).SequenceEqual([1L])).IsTrue();
        await Assert.That(first.HasMore).IsTrue();
        await Assert.That(next.Events.Select(record => record.Position).SequenceEqual([2L])).IsTrue();
        await Assert.That(next.HasMore).IsFalse();
        await Assert.That(streamPage.Events.Select(record => record.Position).SequenceEqual([1L, 2L])).IsTrue();
        await Assert.That(first.Events[0].Data.PayloadJson).DoesNotContain(PrivateValue);
        await Assert.That(first.Events[0].Data.PayloadJson).Contains(PublicValue);
    }

    [Test]
    public async Task AcMp005MissingOrCorruptTopicRecordsFailAndRestoredRecordReads()
    {
        using var database = new TestDatabase();
        ConfigureSources(database);
        PublishTopic(database, [new("topic-a", EventType, "{}")]);
        var source = new EventSourceRef(database.Partition, TopicName, EventSourceKind.Topic);
        var key = TopicEventKey(database, 1);
        var original = database.Store.Read(view => view.ReadOwnedValue(key))!;

        database.Store.Commit((transaction, _) => { transaction.Delete(key); return true; });
        var missing = Assert.ThrowsExactly<KeyLoadException>(() => database.Database.ReadEventSource(ReaderId, new(source)));
        await Assert.That(missing.Code).IsEqualTo(ErrorCode.HistoryUnavailable);

        database.Store.Commit((transaction, _) => { transaction.Put(key, System.Text.Encoding.UTF8.GetBytes(InvalidJson)); return true; });
        var corrupt = Assert.ThrowsExactly<KeyLoadException>(() => database.Database.ReadEventSource(ReaderId, new(source)));
        await Assert.That(corrupt.Code).IsEqualTo(ErrorCode.Corruption);
        database.Store.Commit((transaction, _) => { transaction.Put(key, original); return true; });

        await Assert.That(database.Database.ReadEventSource(ReaderId, new(source)).Events).HasSingleItem();
    }

    [Test]
    public async Task AcMp005WrongPersistedSourceOrPositionIsCorruptionAndCanBeRestored()
    {
        using var database = new TestDatabase();
        ConfigureSources(database);
        PublishTopic(database, [new("topic-a", EventType, "{}")]);
        var source = new EventSourceRef(database.Partition, TopicName, EventSourceKind.Topic);
        var key = TopicEventKey(database, 1);
        var original = database.Store.Read(view => view.ReadOwnedValue(key))!;
        var wrongSource = new EventSourceRef(database.Partition, OtherTopicName, EventSourceKind.Topic);
        var record = NativeSerialization.Deserialize<SourceEventRecord>(original.AsSpan());

        database.Store.Commit((transaction, _) =>
        {
            transaction.PutRecord(key, record with { Source = wrongSource });
            return true;
        });
        var sourceError = Assert.ThrowsExactly<KeyLoadException>(() => database.Database.ReadEventSource(ReaderId, new(source)));
        await Assert.That(sourceError.Code).IsEqualTo(ErrorCode.Corruption);

        database.Store.Commit((transaction, _) =>
        {
            transaction.PutRecord(key, record with { Position = 2 });
            return true;
        });
        var positionError = Assert.ThrowsExactly<KeyLoadException>(() => database.Database.ReadEventSource(ReaderId, new(source)));
        await Assert.That(positionError.Code).IsEqualTo(ErrorCode.Corruption);
        database.Store.Commit((transaction, _) => { transaction.Put(key, original); return true; });

        await Assert.That(database.Database.ReadEventSource(ReaderId, new(source)).Events.Single().Position).IsEqualTo(1L);
    }

    [Test]
    public async Task AcMp005WrongPersistedStreamIdentityOrRevisionIsCorruptionAndCanBeRestored()
    {
        using var database = new TestDatabase();
        ConfigureSources(database);
        AppendStream(database, [new("stream-a", EventType, "{}")]);
        var source = new EventSourceRef(database.Partition, StreamSetName, EventSourceKind.Stream, StreamId);
        var key = StreamEventKey(database, 1);
        var original = database.Store.Read(view => view.ReadOwnedValue(key))!;
        var record = NativeSerialization.Deserialize<EventRecord>(original.AsSpan());

        database.Store.Commit((transaction, _) =>
        {
            transaction.PutRecord(key, record with { Stream = record.Stream with { StreamId = "other-stream" } });
            return true;
        });
        var streamError = Assert.ThrowsExactly<KeyLoadException>(() => database.Database.ReadEventSource(ReaderId, new(source)));
        await Assert.That(streamError.Code).IsEqualTo(ErrorCode.Corruption);

        database.Store.Commit((transaction, _) =>
        {
            transaction.PutRecord(key, record with { Revision = 2 });
            return true;
        });
        var revisionError = Assert.ThrowsExactly<KeyLoadException>(() => database.Database.ReadEventSource(ReaderId, new(source)));
        await Assert.That(revisionError.Code).IsEqualTo(ErrorCode.Corruption);
        database.Store.Commit((transaction, _) => { transaction.Put(key, original); return true; });

        await Assert.That(database.Database.ReadEventSource(ReaderId, new(source)).Events).HasSingleItem();
    }

    private static void ConfigureSources(TestDatabase database)
    {
        ConfigureSource(database, TopicName, ResourceKind.Topic);
        ConfigureSource(database, StreamSetName, ResourceKind.StreamSet);
        var reader = new PrincipalRecord(ReaderId, database.Partition.TenantId,
            [new(database.Partition.DatabaseId, TopicName, Capability.TopicsRead),
             new(database.Partition.DatabaseId, StreamSetName, Capability.EventsRead)], []);
        database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(reader),
            time: SourceReadBusinessTime.Next(database)).Get<PrincipalRecord>();
    }

    private static void ConfigureSource(TestDatabase database, string name, ResourceKind kind)
    {
        var resource = new ResourceDefinition(name, kind, database.Partition.TransactionDomainId)
        {
            FieldPolicies = [new(SecretPath, "private")]
        };
        database.Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(database.Partition.TenantId, database.Partition.DatabaseId, resource),
            time: SourceReadBusinessTime.Next(database)).Get<ResourceDefinition>();
    }

    private static void PublishTopic(TestDatabase database, EventData[] events)
    {
        var id = Guid.NewGuid();
        database.Submit(OperationKind.Batch, new CommandRequest(id, database.Partition,
            [new PublishTopic(TopicName, [.. events])]), id: id, time: SourceReadBusinessTime.Next(database)).Get<CommitReceipt>();
    }

    private static void AppendStream(TestDatabase database, EventData[] events)
    {
        var id = Guid.NewGuid();
        database.Submit(OperationKind.Batch, new CommandRequest(id, database.Partition,
            [new AppendEvents(StreamSetName, StreamId, [.. events], ExpectedStreamRevision.NoStream)]), id: id,
            time: SourceReadBusinessTime.Next(database)).Get<CommitReceipt>();
    }

    private static byte[] TopicEventKey(TestDatabase database, long position)
        => KeySpace.Partition(TopicEventSpace, database.Partition, TopicName, 1L, position);

    private static byte[] StreamEventKey(TestDatabase database, long position)
        => KeySpace.Partition(StreamEventSpace, database.Partition, StreamSetName, StreamId, 1L, position);
}
