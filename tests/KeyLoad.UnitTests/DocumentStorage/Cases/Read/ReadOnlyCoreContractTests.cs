using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.DocumentStorage;

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
    public async Task AcRoc005NullCommandMutationsBecomePersistedValidationAndFollowingCommandSucceeds()
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
        var rejected = database.Database.Apply(new(id, OperationKind.Batch, RootPrincipal,
            TimeProvider.System.GetUtcNow(), payload));

        await Assert.That(rejected.Error).IsEqualTo(ErrorCode.Validation);
        await Assert.That(database.Store.Read(view => view.GetRecord<StoredOutcome>(KeySpace.UnknownOutcome(RootPrincipal, id)))!.Result.Error).IsEqualTo(ErrorCode.Validation);
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
