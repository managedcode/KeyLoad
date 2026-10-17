using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal sealed class BlobHeadStateAuthorizationTests
{
    private const string AliceId = "blob-state-owner";
    private const string NoManageId = "blob-state-no-manage";
    private const string AliceOwnerId = "owner-alice";
    private const string BobOwnerId = "owner-bob";
    private const string DatabaseName = "database";
    private const string ResourceName = "blobs";
    private const string BlobId = "head-state-acl";
    private const string PartSpace = "blob-part-v1";
    private const string GuidFormat = "N";
    private const int FirstPartOrdinal = 0;
    private const int TailPartOrdinal = 1;
    private const int ReclaimPartLimit = 2;
    private const long InitialRevision = 0;
    private const long CurrentRevision = 1;
    private const int TailLength = 1;
    private const Capability BlobCapabilities = Capability.BlobRead | Capability.BlobWrite
        | Capability.BlobDelete | Capability.BlobManage;
    private static readonly byte[] FirstPart = Enumerable.Repeat((byte)0x35, BlobLimits.RawPartBytes).ToArray();
    private static readonly byte[] TailPart = [0xa1];
    private static readonly byte[] NewCurrentBytes = [0x61, 0x62, 0x63];

    [Test]
    public async Task AcBlob003UploadStateAuthoritySurvivesHeadOwnerChangeButHeadMutationsDoNot()
    {
        using var database = new TestDatabase();
        BlobStorageTestSupport.Configure(database);
        ConfigureAlice(database);
        var blob = BlobStorageTestSupport.Blob(database, BlobId);
        var operations = new BlobStorageOperations(database.Database);
        var aliceUploadId = Guid.NewGuid();
        StartAliceUpload(database, blob, aliceUploadId);

        var currentUploadId = Guid.NewGuid();
        var currentMetadata = Publish(database, blob, currentUploadId, NewCurrentBytes, new(BobOwnerId));
        await Assert.That(currentMetadata.Revision).IsEqualTo(CurrentRevision);
        await AssertStateRowRemainsWritable(database, operations, blob, aliceUploadId);
        await AssertCurrentHeadActionsRequireHeadAuthority(database, blob, aliceUploadId);

        await AssertCurrentVersionCannotBeReclaimed(database, operations, blob, currentUploadId);
        await AssertStateCanBeAbortedAndReclaimed(database, operations, blob, aliceUploadId);
    }

    private static void StartAliceUpload(TestDatabase database, BlobRef blob, Guid uploadId)
    {
        var commandId = Guid.NewGuid();
        Begin(database, AliceId, blob, uploadId, FirstPart.Length + TailLength,
            InitialRevision, new(AliceOwnerId), commandId).Get<BlobCommitResult<BlobUploadInfo>>();
        Write(database, AliceId, blob, uploadId, FirstPartOrdinal, FirstPart);
    }

    private static async Task AssertStateRowRemainsWritable(TestDatabase database,
        BlobStorageOperations operations, BlobRef blob, Guid uploadId)
    {
        Write(database, AliceId, blob, uploadId, TailPartOrdinal, TailPart);
        var info = operations.UploadInfo(AliceId, new(blob, uploadId));
        await Assert.That(info?.StoredBytes).IsEqualTo(FirstPart.Length + TailLength);
        await Assert.That(Reclaim(database, AliceId, blob, uploadId, Guid.NewGuid()).Error)
            .IsEqualTo(ErrorCode.Conflict);
    }

    private static async Task AssertCurrentHeadActionsRequireHeadAuthority(TestDatabase database,
        BlobRef blob, Guid uploadId)
    {
        var deniedBegin = Begin(database, AliceId, blob, Guid.NewGuid(), TailLength,
            CurrentRevision, new(AliceOwnerId), Guid.NewGuid());
        var completeId = Guid.NewGuid();
        var deniedComplete = Apply(database, OperationKind.CompleteBlobUpload, AliceId, completeId,
            new CompleteBlobUploadRequest(completeId, blob, uploadId, FinalHash(database, blob, uploadId)));
        var deleteId = Guid.NewGuid();
        var deniedDelete = Apply(database, OperationKind.DeleteBlob, AliceId, deleteId,
            new DeleteBlobRequest(deleteId, blob, CurrentRevision));
        await Assert.That(deniedBegin.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(deniedComplete.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(deniedDelete.Error).IsEqualTo(ErrorCode.PermissionDenied);
    }

    private static async Task AssertCurrentVersionCannotBeReclaimed(TestDatabase database,
        BlobStorageOperations operations, BlobRef blob, Guid currentUploadId)
    {
        var currentReclaim = Reclaim(database, "root", blob, currentUploadId, Guid.NewGuid());
        await Assert.That(currentReclaim.Error).IsEqualTo(ErrorCode.Conflict);
        var unchangedHead = operations.Metadata("root", new(blob));
        await Assert.That(unchangedHead?.Revision).IsEqualTo(CurrentRevision);
        await Assert.That(unchangedHead?.VersionId == currentUploadId).IsTrue();
    }

    private static async Task AssertStateCanBeAbortedAndReclaimed(TestDatabase database,
        BlobStorageOperations operations, BlobRef blob, Guid uploadId)
    {
        ConfigurePrincipal(database, Principal(NoManageId, AliceOwnerId, Capability.BlobRead | Capability.BlobWrite));
        await Assert.That(Reclaim(database, NoManageId, blob, uploadId, Guid.NewGuid()).Error)
            .IsEqualTo(ErrorCode.PermissionDenied);
        ConfigureAlice(database, revoked: true, policyEpoch: 2);
        await Assert.That(Reclaim(database, AliceId, blob, uploadId, Guid.NewGuid()).Error)
            .IsEqualTo(ErrorCode.Unauthenticated);
        ConfigureAlice(database, revoked: false, policyEpoch: 3);

        var abortId = Guid.NewGuid();
        Apply(database, OperationKind.AbortBlobUpload, AliceId, abortId,
            new AbortBlobUploadRequest(abortId, blob, uploadId)).Get<BlobCommitResult<BlobUploadInfo>>();
        var reclaimed = Reclaim(database, AliceId, blob, uploadId, Guid.NewGuid())
            .Get<BlobCommitResult<BlobReclaimResult>>();
        await Assert.That(reclaimed.Value.Complete).IsTrue();
        await Assert.That(reclaimed.Value.DeletedParts).IsEqualTo(ReclaimPartLimit);
        await Assert.That(Read(database, PartKey(blob, uploadId, FirstPartOrdinal))).IsNull();
        await Assert.That(operations.Read("root", new(blob, CurrentRevision, 0, NewCurrentBytes.Length))
            .Bytes.Span.SequenceEqual(NewCurrentBytes)).IsTrue();
    }

    private static string FinalHash(TestDatabase database, BlobRef blob, Guid uploadId)
    {
        var hash = BlobIntegrity.InitialHash(database.Store.Identity.Incarnation,
            blob, uploadId, FirstPart.Length + TailLength);
        hash = BlobIntegrity.NextHash(hash, FirstPartOrdinal, FirstPart.Length, BlobIntegrity.PartHash(FirstPart));
        return BlobIntegrity.NextHash(hash, TailPartOrdinal, TailPart.Length, BlobIntegrity.PartHash(TailPart));
    }

    private static void ConfigureAlice(TestDatabase database, bool revoked = false, long policyEpoch = 1) =>
        ConfigurePrincipal(database, Principal(AliceId, AliceOwnerId, BlobCapabilities, revoked, policyEpoch));

    private static PrincipalRecord Principal(string id, string ownerId, Capability capabilities,
        bool revoked = false, long policyEpoch = 1) => new(id, "tenant",
            [new(DatabaseName, ResourceName, capabilities)], [])
        {
            OwnerId = ownerId,
            RestrictRows = true,
            Revoked = revoked,
            PolicyEpoch = policyEpoch
        };

    private static void ConfigurePrincipal(TestDatabase database, PrincipalRecord principal) =>
        database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal))
            .Get<PrincipalRecord>();

    private static BlobMetadata Publish(TestDatabase database, BlobRef blob, Guid uploadId,
        ReadOnlyMemory<byte> bytes, RowAccess access)
    {
        var commandId = Guid.NewGuid();
        Begin(database, "root", blob, uploadId, bytes.Length, InitialRevision, access, commandId)
            .Get<BlobCommitResult<BlobUploadInfo>>();
        Write(database, "root", blob, uploadId, FirstPartOrdinal, bytes);
        var integrity = BlobIntegrity.InitialHash(database.Store.Identity.Incarnation, blob, uploadId, bytes.Length);
        integrity = BlobIntegrity.NextHash(integrity, FirstPartOrdinal, bytes.Length, BlobIntegrity.PartHash(bytes.Span));
        var completeId = Guid.NewGuid();
        return Apply(database, OperationKind.CompleteBlobUpload, "root", completeId,
            new CompleteBlobUploadRequest(completeId, blob, uploadId, integrity))
            .Get<BlobCommitResult<BlobMetadata>>().Value;
    }

    private static OperationResult Begin(TestDatabase database, string principal, BlobRef blob,
        Guid uploadId, long length, long expectedRevision, RowAccess access, Guid commandId) =>
        Apply(database, OperationKind.BeginBlobUpload, principal, commandId,
            new BeginBlobUploadRequest(commandId, blob, uploadId, length, expectedRevision, access));

    private static void Write(TestDatabase database, string principal, BlobRef blob,
        Guid uploadId, int ordinal, ReadOnlyMemory<byte> bytes)
    {
        var commandId = Guid.NewGuid();
        Apply(database, OperationKind.WriteBlobPart, principal, commandId,
            new WriteBlobPartRequest(commandId, blob, uploadId, ordinal, bytes, BlobIntegrity.PartHash(bytes.Span)))
            .Get<BlobCommitResult<BlobUploadInfo>>();
    }

    private static OperationResult Reclaim(TestDatabase database, string principal,
        BlobRef blob, Guid uploadId, Guid commandId) =>
        Apply(database, OperationKind.ReclaimBlob, principal, commandId,
            new ReclaimBlobRequest(commandId, blob, uploadId, ReclaimPartLimit));

    private static OperationResult Apply<T>(TestDatabase database, OperationKind kind,
        string principal, Guid commandId, T payload) =>
        database.Submit(kind, payload, principal, commandId);

    private static byte[] PartKey(BlobRef blob, Guid uploadId, int ordinal) =>
        KeySpace.Partition(PartSpace, blob.Partition, blob.Resource, blob.Id,
            uploadId.ToString(GuidFormat), (long)ordinal);

    private static byte[]? Read(TestDatabase database, byte[] key) =>
        database.Store.Read(view => view.ReadOwnedValue(key));
}
