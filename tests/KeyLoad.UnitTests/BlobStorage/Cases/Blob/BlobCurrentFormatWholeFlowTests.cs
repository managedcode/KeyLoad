using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal sealed class BlobCurrentFormatWholeFlowTests
{
    [Test]
    public async Task AcBlob007CurrentResourcePersistsCompleteLiteralJsonAndStableReplayThenHealthyDocument()
    {
        using var database = new TestDatabase();
        var id = Guid.NewGuid();
        var request = new ConfigureResourceRequest(database.Partition.TenantId, database.Partition.DatabaseId,
            new ResourceDefinition(BlobCurrentFormatFixture.Collection, ResourceKind.Collection, database.Partition.TransactionDomainId));
        var original = database.Submit(OperationKind.ConfigureResource, request, id: id);
        var configured = original.Get<ResourceDefinition>();
        await Assert.That(System.Text.Encoding.UTF8.GetString(JsonDefaults.Serialize(configured)))
            .IsEqualTo(BlobCurrentFormatFixture.CollectionJson);
        var persisted = database.Store.Read(view => database.Database.Resource(view, database.Partition,
            BlobCurrentFormatFixture.Collection, ResourceKind.Collection));
        await Assert.That(System.Text.Encoding.UTF8.GetString(JsonDefaults.Serialize(persisted)))
            .IsEqualTo(BlobCurrentFormatFixture.CollectionJson);
        var image = QueueWholeFlowStorage.Bytes(database.Store);
        var cut = database.Store.Position;
        var replay = database.Submit(OperationKind.ConfigureResource, request, id: id);
        await Assert.That(NativeSerialization.Serialize(replay).SequenceEqual(NativeSerialization.Serialize(original))).IsTrue();
        await Assert.That(database.Store.Position).IsEqualTo(cut);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
        database.Commit(new PutDocument(BlobCurrentFormatFixture.Collection, BlobCurrentFormatFixture.DocumentId,
            BlobCurrentFormatFixture.DocumentJson));
        var readImage = QueueWholeFlowStorage.Bytes(database.Store);
        var readCut = database.Store.Position;
        var document = database.Database.GetDocument(BlobCurrentFormatFixture.Principal,
            new(database.Partition, BlobCurrentFormatFixture.Collection, BlobCurrentFormatFixture.DocumentId));
        var expected = new DocumentResult(new(database.Partition, BlobCurrentFormatFixture.Collection,
            BlobCurrentFormatFixture.DocumentId), BlobCurrentFormatFixture.OriginalRevision, BlobCurrentFormatFixture.DocumentJson, false, []);
        await Assert.That(JsonDefaults.Serialize(document).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(database.Store.Position).IsEqualTo(readCut);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(readImage, CollectionOrdering.Matching);
        await BlobCurrentFormatIdentities.StableIdentitiesAsync();
    }

    [Test]
    public async Task AcBlob007DefaultNativeLimitRejectsThenPublishReplayPartialReadAndReopenAreComplete()
    {
        using var database = new TestDatabase();
        BlobStorageTestSupport.Configure(database);
        var blob = BlobStorageTestSupport.Blob(database, BlobCurrentFormatFixture.BlobId);
        await BlobCurrentFormatOperations.RejectDefaultLimitAsync(database, blob);
        var publication = await BlobCurrentFormatOperations.PublishAsync(database, blob);
        await BlobCurrentFormatOperations.VerifyAsync(database.Database, publication.Metadata);
        var image = QueueWholeFlowStorage.Bytes(database.Store);
        var cut = database.Store.Position;
        var replay = database.Submit(OperationKind.CompleteBlobUpload, publication.Request,
            id: publication.Request.CommandId).Get<BlobCommitResult<BlobMetadata>>();
        await Assert.That(NativeSerialization.Serialize(replay).SequenceEqual(NativeSerialization.Serialize(publication.Result))).IsTrue();
        await Assert.That(database.Store.Position).IsEqualTo(cut);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
        database.Store.Dispose();
        using var reopened = new KeyLoad.Storage.ZoneTree.ZoneTreeStore(new(database.Directory),
            UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var owner = BlobCurrentFormatFixture.Reopened(reopened);
        await BlobCurrentFormatOperations.VerifyAsync(owner, publication.Metadata);
        await Assert.That(reopened.Position).IsEqualTo(cut);
        await Assert.That(QueueWholeFlowStorage.Bytes(reopened)).IsEquivalentTo(image, CollectionOrdering.Matching);
        await BlobCurrentFormatIdentities.StableIdentitiesAsync();
    }
}
