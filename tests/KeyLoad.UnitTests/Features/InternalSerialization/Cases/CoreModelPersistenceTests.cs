using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class CoreModelPersistenceTests
{
    private const string Collection = "native-documents";
    private const string RecordId = "native-record";
    private const string StreamSet = "native-streams";
    private const string EventType = "NativeEvent";
    private const string Purpose = "native-store-test";
    private const string RawJson = "{\"number\":1e+01,\"precise\":12345678901234567890123456789,\"text\":\"界λ\"}";
    private static readonly DateTimeOffset Timestamp = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task AcIs002DocumentsEventsAndMessagesKeepExactUserJsonInNativeRecords()
    {
        using var database = new TestDatabase();
        var entity = new EntityRef(database.Partition, Collection, RecordId);
        var document = new DocumentRecord(entity, 1, RawJson, new(), Timestamp);
        var storedDocument = RoundTrip(database, document);
        await Assert.That(storedDocument).IsEqualTo(document);
        var stream = new StreamRef(database.Partition, StreamSet, RecordId);
        var item = new EventData(RecordId, EventType, RawJson, HeadersJson: RawJson);
        var eventRecord = new EventRecord(stream, 1, 1, item, Timestamp);
        await Assert.That(RoundTrip(database, eventRecord)).IsEqualTo(eventRecord);
        var body = new MessageBody(RecordId, RawJson, RawJson, null, JsonData.Fingerprint(item));
        await Assert.That(RoundTrip(database, body)).IsEqualTo(body);
    }

    [Test]
    public async Task AcIs002GraphSamplesAndPrivateCoreRecordsUseOwningGeneratedContracts()
    {
        using var database = new TestDatabase();
        var entity = new EntityRef(database.Partition, Collection, RecordId);
        var edge = new EdgeRecord(RecordId, entity, entity, EventType, RawJson, 1);
        await Assert.That(RoundTrip(database, edge)).IsEqualTo(edge);
        var sample = new SampleRecord(RecordId, new(RecordId, Timestamp, -0.0), 1, RawJson);
        var restoredSample = RoundTrip(database, sample);
        await Assert.That(restoredSample).IsEqualTo(sample);
        await Assert.That(BitConverter.DoubleToInt64Bits(restoredSample.Sample.Value))
            .IsEqualTo(BitConverter.DoubleToInt64Bits(sample.Sample.Value));
        var identity = new EventIdentity(RecordId, 1, 1);
        await Assert.That(RoundTrip(database, identity)).IsEqualTo(identity);
        var head = new TopicHead(1, 1, 1, NativeSerialization.Measure(sample));
        await Assert.That(RoundTrip(database, head)).IsEqualTo(head);
        await Assert.That(RoundTripScalar(database, Timestamp)).IsEqualTo(Timestamp);
        await Assert.That(RoundTripScalar(database, 1L)).IsEqualTo(1L);
        await Assert.That(RoundTripScalar(database, false)).IsFalse();
    }

    [Test]
    public async Task AcIs002SearchAndResourceSchemaKeepNativeVectorAndJsonContracts()
    {
        using var database = new TestDatabase();
        var space = new VectorSpace(RecordId, 2, DistanceMetric.Cosine, EventType, RecordId);
        var vector = new VectorRecord(RecordId, EventType, space, [1.25f, -0.5f], 1);
        var restored = RoundTrip(database, vector);
        await Assert.That(restored.Space).IsEqualTo(space);
        await Assert.That(restored.DocumentRevision).IsEqualTo(vector.DocumentRevision);
        await Assert.That(restored.Values).IsEquivalentTo(vector.Values, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        var resource = database.Configure(Collection, ResourceKind.Collection);
        var restoredResource = database.Store.Read(view => view.GetRecord<ResourceDefinition>(
            KeySpace.Resource(database.Partition.TenantId, database.Partition.DatabaseId, Collection)))!;
        await Assert.That(restoredResource.Name).IsEqualTo(resource.Name);
        await Assert.That(restoredResource.Kind).IsEqualTo(resource.Kind);
        await Assert.That(restoredResource.TransactionDomainId).IsEqualTo(resource.TransactionDomainId);
        await Assert.That(restoredResource.Indexes).IsEmpty();
        await Assert.That(restoredResource.FieldPolicies).IsEmpty();
    }

    [Test]
    public async Task AcIs002MalformedNativeRecordFailsAtRealStoreReadThenRestores()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, RecordId, RawJson));
        var key = KeySpace.Partition(DocumentSpace, database.Partition, Collection, RecordId);
        var original = database.Store.Read(view => view.ReadOwnedValue(key))!;
        database.Store.Commit((transaction, _) => { transaction.Put(key, original[..^1]); return true; });
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            database.Database.GetDocument(RootPrincipal, new(database.Partition, Collection, RecordId)));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        database.Store.Commit((transaction, _) => { transaction.Put(key, original); return true; });
        await Assert.That(database.Database.GetDocument(RootPrincipal, new(database.Partition, Collection, RecordId))!.Json)
            .IsEqualTo(JsonData.Validate(RawJson, database.Database.Limits));
    }

    private const string DocumentSpace = "document";
    private const string RootPrincipal = "root";

    private static T RoundTripScalar<T>(TestDatabase database, T value) where T : struct
    {
        var key = KeyCodec.Encode(Purpose, typeof(T).FullName!);
        database.Store.Commit((transaction, _) => { transaction.PutRecord(key, value); return true; });
        return database.Store.Read(view => NativeSerialization.Deserialize<T>(view.ReadOwnedValue(key)!));
    }

    private static T RoundTrip<T>(TestDatabase database, T value) where T : class
    {
        var key = KeyCodec.Encode(Purpose, typeof(T).FullName!);
        database.Store.Commit((transaction, _) => { transaction.PutRecord(key, value); return true; });
        return database.Store.Read(view => view.GetRecord<T>(key))!;
    }
}
