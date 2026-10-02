using KeyLoad.Core.Features.BlobStorage;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal sealed class BlobAuthorizationTests
{
    private const string PrincipalAlice = "blob-alice";
    private const string PrincipalBob = "blob-bob";
    private const string PrincipalCharlie = "blob-charlie";
    private const string OwnerAlice = "alice-owner";
    private const string OwnerBob = "bob-owner";
    private const string PublishedBlobId = "owned-published";
    private const string ActiveBlobId = "creator-only-upload";
    private const string ResourceName = "blobs";
    private const string DatabaseName = "database";
    private const Capability ReadWrite = Capability.BlobRead | Capability.BlobWrite;
    private static readonly byte[] PublishedBytes = [0x41, 0x42, 0x43];
    private static readonly byte[] ActiveBytes = [0x51];

    [Test]
    public async Task AcBlob003CurrentPrincipalRowAndCreatorAuthorityAreRecheckedForEveryRead()
    {
        using var db = new TestDatabase();
        BlobStorageTestSupport.Configure(db);
        ConfigurePrincipal(db, Principal(PrincipalAlice, OwnerAlice, restrictRows: true));
        ConfigurePrincipal(db, Principal(PrincipalBob, OwnerBob, restrictRows: true));
        ConfigurePrincipal(db, Principal(PrincipalCharlie, OwnerBob, restrictRows: false));
        var operations = new BlobStorageOperations(db.Database);
        var published = BlobStorageTestSupport.Blob(db, PublishedBlobId);
        var metadata = PublishOwned(db, published);
        var active = BlobStorageTestSupport.Blob(db, ActiveBlobId);
        var activeUpload = StartActiveUpload(db, active);

        var hiddenMetadata = Assert.ThrowsExactly<KeyLoadException>(
            () => operations.Metadata(PrincipalBob, new(published)));
        var hiddenRange = Assert.ThrowsExactly<KeyLoadException>(
            () => operations.Read(PrincipalBob, new(published, metadata.Revision, 0, PublishedBytes.Length)));
        var wrongCreator = Assert.ThrowsExactly<KeyLoadException>(
            () => operations.UploadInfo(PrincipalCharlie, new(active, activeUpload)));
        await Assert.That(hiddenMetadata.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(hiddenRange.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(wrongCreator.Code).IsEqualTo(ErrorCode.PermissionDenied);

        ConfigurePrincipal(db, Principal(PrincipalAlice, OwnerAlice, restrictRows: true) with
        {
            Revoked = true,
            PolicyEpoch = 2
        });
        var revoked = Assert.ThrowsExactly<KeyLoadException>(
            () => operations.Metadata(PrincipalAlice, new(published)));
        await Assert.That(revoked.Code).IsEqualTo(ErrorCode.Unauthenticated);
    }

    private static BlobMetadata PublishOwned(TestDatabase database, BlobRef blob)
    {
        var uploadId = Guid.NewGuid();
        var beginId = Guid.NewGuid();
        var access = new RowAccess(OwnerAlice);
        var begun = new BeginBlobUploadRequest(beginId, blob, uploadId, PublishedBytes.Length, 0, access);
        Apply(database, OperationKind.BeginBlobUpload, PrincipalAlice, beginId, begun)
            .Get<BlobCommitResult<BlobUploadInfo>>();
        var writeId = Guid.NewGuid();
        var write = new WriteBlobPartRequest(writeId, blob, uploadId, 0,
            PublishedBytes, BlobIntegrity.PartHash(PublishedBytes));
        Apply(database, OperationKind.WriteBlobPart, PrincipalAlice, writeId, write)
            .Get<BlobCommitResult<BlobUploadInfo>>();
        var chain = BlobIntegrity.InitialHash(database.Store.Identity.Incarnation, blob, uploadId, PublishedBytes.Length);
        chain = BlobIntegrity.NextHash(chain, 0, PublishedBytes.Length, BlobIntegrity.PartHash(PublishedBytes));
        var completeId = Guid.NewGuid();
        return Apply(database, OperationKind.CompleteBlobUpload, PrincipalAlice, completeId,
            new CompleteBlobUploadRequest(completeId, blob, uploadId, chain))
            .Get<BlobCommitResult<BlobMetadata>>().Value;
    }

    private static Guid StartActiveUpload(TestDatabase database, BlobRef blob)
    {
        var uploadId = Guid.NewGuid();
        var beginId = Guid.NewGuid();
        var access = new RowAccess(OwnerAlice);
        Apply(database, OperationKind.BeginBlobUpload, PrincipalAlice, beginId,
            new BeginBlobUploadRequest(beginId, blob, uploadId, ActiveBytes.Length, 0, access))
            .Get<BlobCommitResult<BlobUploadInfo>>();
        return uploadId;
    }

    private static PrincipalRecord Principal(string id, string ownerId, bool restrictRows) =>
        new(id, "tenant", [new(DatabaseName, ResourceName, ReadWrite)], [])
        {
            OwnerId = ownerId,
            RestrictRows = restrictRows
        };

    private static void ConfigurePrincipal(TestDatabase database, PrincipalRecord principal) =>
        database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal))
            .Get<PrincipalRecord>();

    private static OperationResult Apply<T>(TestDatabase database, OperationKind kind,
        string principalId, Guid commandId, T payload) =>
        database.Submit(kind, payload, principalId, commandId);
}
