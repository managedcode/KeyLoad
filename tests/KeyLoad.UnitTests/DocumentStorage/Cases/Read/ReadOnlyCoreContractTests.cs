using System.Collections.Immutable;
using System.Text.Json;

namespace KeyLoad.UnitTests.DocumentStorage;

internal sealed class ReadOnlyCoreContractTests
{
    private const string DocumentCollection = "orders";
    private const string HealthyDocumentId = "valid-after-rejection";
    private const string EmptyDocumentJson = "{}";
    private const string RootPrincipal = "root";
    private const string EventSet = "events";
    private const string Topic = "topic";
    private const string SeriesSet = "series";

    [Test]
    public async Task AcRoc005DefaultCommandMutationCollectionRejectsBeforeWriteAndLeavesStoreHealthy()
    {
        using var database = new TestDatabase();
        database.Configure(DocumentCollection, ResourceKind.Collection);
        var command = new CommandRequest(Guid.NewGuid(), database.Partition, default(ImmutableArray<Mutation>));

        Assert.ThrowsExactly<JsonException>(() => JsonDefaults.Serialize(command));
        database.Commit(new PutDocument(DocumentCollection, HealthyDocumentId, EmptyDocumentJson));
        await Assert.That(database.Database.GetDocument(RootPrincipal,
            new(database.Partition, DocumentCollection, HealthyDocumentId))).IsNotNull();
    }

    [Test]
    public async Task AcRoc005NullCommandMutationsRejectAtAdmissionAndFollowingCommandSucceeds()
    {
        using var database = new TestDatabase();
        database.Configure(DocumentCollection, ResourceKind.Collection);
        var id = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new
        {
            commandId = id,
            partition = database.Partition,
            mutations = (object?)null
        }, JsonDefaults.Options);
        var before = global::KeyLoad.UnitTests.Messaging.QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        var rejected = Assert.ThrowsExactly<KeyLoadException>(() =>
            database.Database.CreateCommand(OperationKind.Batch, id, RootPrincipal, TimeProvider.System.GetUtcNow(), payload));
        await Assert.That(rejected.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(rejected.Message).IsEqualTo("The operation contains invalid protocol JSON.");
        await Assert.That(global::KeyLoad.UnitTests.Messaging.QueueWholeFlowStorage.Bytes(database.Store))
            .IsEquivalentTo(before, global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        database.Commit(new PutDocument(DocumentCollection, HealthyDocumentId, EmptyDocumentJson));
        await Assert.That(database.Database.GetDocument(RootPrincipal,
            new(database.Partition, DocumentCollection, HealthyDocumentId))).IsNotNull();
    }

    [Test]
    public async Task AcRoc005DefaultNestedMutationCollectionsRejectBeforeApply()
    {
        using var database = new TestDatabase();
        Mutation[] invalid =
        [
            new PatchDocument(DocumentCollection, HealthyDocumentId, default, 1),
            new AppendEvents(EventSet, HealthyDocumentId, default, ExpectedStreamRevision.NoStream),
            new PublishTopic(Topic, default),
            new AppendSamples(SeriesSet, HealthyDocumentId, default)
        ];

        foreach (var mutation in invalid)
        {
            var command = new CommandRequest(Guid.NewGuid(), database.Partition, [mutation]);
            Assert.ThrowsExactly<JsonException>(() => JsonDefaults.Serialize(command));
        }

        database.Configure(DocumentCollection, ResourceKind.Collection);
        database.Commit(new PutDocument(DocumentCollection, HealthyDocumentId, EmptyDocumentJson));
        await Assert.That(database.Database.GetDocument(RootPrincipal,
            new(database.Partition, DocumentCollection, HealthyDocumentId))).IsNotNull();
    }
}
