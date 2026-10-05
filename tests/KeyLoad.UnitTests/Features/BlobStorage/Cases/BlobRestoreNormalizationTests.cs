using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Security;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal sealed class BlobRestoreNormalizationTests
{
    private const string TestDirectoryPrefix = "keyload-blob-restore-test-";
    private const string SourceDirectoryName = "source";
    private const string BackupDirectoryName = "backup";
    private const string RestoredDirectoryName = "restored";
    private const string PrincipalId = "root";
    private const string ResourceName = "blobs";
    private const string PublishedBlobId = "published-before-restore";
    private const string ActiveBlobId = "active-at-restore";
    private const string PostRestoreBlobId = "post-restore-upload";
    private const int ActivePartOrdinal = 0;
    private const int PostRestoreLength = 1;
    private const int ResourceKeyLimit = 3;
    private const int VersionLimit = 4;
    private const int ActiveUploadLimit = 1;
    private const int UploadTtlSeconds = 3_600;
    private const int AcceptedPartLength = BlobLimits.RawPartBytes;
    private static readonly byte[] PostRestoreBytes = [0xc1];
    private static readonly byte[] PublishedBytes = [0x42, 0x6c, 0x6f, 0x62, 0x21];

    [Test]
    public async Task AcBlob004RestoreRebindsAuthorityAndPreservesPublishedBytesAndActivePartForReclaim()
    {
        var root = Path.Combine(Path.GetTempPath(), TestDirectoryPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var backup = PrepareBackup(root);
            var restoredIdentity = ZoneTreeStore.Restore(backup.BackupDirectory,                 Path.Combine(root, RestoredDirectoryName), UnitExecutionOptions.StorageExecution(), backup.TargetIncarnation);
            using var restoredStore = new ZoneTreeStore(new(Path.Combine(root, RestoredDirectoryName)), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
            var restoredDatabase = new DatabaseEngine(restoredStore, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource());
            var operations = new BlobStorageOperations(restoredDatabase);

            await Assert.That(restoredIdentity.Incarnation).IsEqualTo(backup.TargetIncarnation);
            await Assert.That(restoredIdentity.Incarnation).IsNotEqualTo(backup.SourceIncarnation);
            operations.NormalizeRestoredStore();
            await AssertPublishedObject(operations, backup);
            await AssertAbortedUploadRetained(operations, restoredStore, backup);
            await AssertUnusedReservationWasReleased(restoredDatabase, operations, backup);
            await AssertOldUploadOutcomeWasInvalidated(restoredDatabase, backup);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static BlobRestoreBackup PrepareBackup(string root)
    {
        var source = Path.Combine(root, SourceDirectoryName);
        var backupDirectory = Path.Combine(root, BackupDirectoryName);
        using var database = new TestDatabase(directory: source);
        ConfigureBoundedBlobResource(database);

        var publishedBlob = BlobStorageTestSupport.Blob(database, PublishedBlobId);
        var publishedUploadId = Guid.NewGuid();
        BlobStorageTestSupport.Begin(database, publishedBlob, publishedUploadId, PublishedBytes.Length, 0);
        BlobStorageTestSupport.Write(database, publishedBlob, publishedUploadId, 0, PublishedBytes);
        var publishedHash = Chain(database, publishedBlob, publishedUploadId, PublishedBytes);
        var publishedMetadata = BlobStorageTestSupport.Complete(database, publishedBlob, publishedUploadId, publishedHash).Value;

        var activeBlob = BlobStorageTestSupport.Blob(database, ActiveBlobId);
        var activeUploadId = Guid.NewGuid();
        var activeBeginId = Guid.NewGuid();
        var activePartId = Guid.NewGuid();
        var acceptedPart = Enumerable.Range(0, AcceptedPartLength).Select(static value => (byte)(value % 239)).ToArray();
        BlobStorageTestSupport.Begin(database, activeBlob, activeUploadId, AcceptedPartLength + 1, 0, activeBeginId);
        BlobStorageTestSupport.Write(database, activeBlob, activeUploadId, ActivePartOrdinal, acceptedPart, activePartId);
        var sourceIncarnation = database.Store.Identity.Incarnation;
        var targetIncarnation = Guid.NewGuid();
        database.Store.CreateBackup(backupDirectory);

        return new(backupDirectory, sourceIncarnation, targetIncarnation, publishedBlob,
            PublishedBytes, publishedMetadata.Revision, publishedMetadata.IntegrityHash!,
            activeBlob, activeUploadId, activeBeginId, activePartId, acceptedPart);
    }

    private static async Task AssertPublishedObject(BlobStorageOperations operations, BlobRestoreBackup backup)
    {
        var metadata = operations.Metadata(PrincipalId, new(backup.PublishedBlob));
        var read = operations.Read(PrincipalId,
            new(backup.PublishedBlob, backup.PublishedRevision, 0, backup.PublishedBytes.Length));

        await Assert.That(metadata!.IntegrityHash).IsEqualTo(backup.PublishedIntegrityHash);
        await Assert.That(metadata.Revision).IsEqualTo(backup.PublishedRevision);
        await Assert.That(read.Metadata).IsEqualTo(metadata);
        await Assert.That(read.Bytes.Span.SequenceEqual(backup.PublishedBytes)).IsTrue();
    }

    private static async Task AssertAbortedUploadRetained(BlobStorageOperations operations,
        ZoneTreeStore store, BlobRestoreBackup backup)
    {
        var upload = operations.UploadInfo(PrincipalId, new(backup.ActiveBlob, backup.ActiveUploadId));
        var rawPartKey = KeyCodec.Encode("blob-part-v1", backup.ActiveBlob.Partition.TenantId,
            backup.ActiveBlob.Partition.DatabaseId, backup.ActiveBlob.Partition.TransactionDomainId,
            backup.ActiveBlob.Partition.PartitionKey, backup.ActiveBlob.Resource, backup.ActiveBlob.Id,
            backup.ActiveUploadId.ToString("N"), (long)ActivePartOrdinal);
        var retainedPart = store.Read(view => view.ReadOwnedValue(rawPartKey));

        await Assert.That(upload!.Status).IsEqualTo(BlobUploadStatus.Aborted);
        await Assert.That(upload.StoredBytes).IsEqualTo(AcceptedPartLength);
        await Assert.That(upload.DeclaredLength).IsEqualTo(AcceptedPartLength + 1L);
        await Assert.That(retainedPart!.SequenceEqual(backup.AcceptedPart)).IsTrue();
    }

    private static async Task AssertUnusedReservationWasReleased(DatabaseEngine database,
        BlobStorageOperations operations, BlobRestoreBackup backup)
    {
        var nextBlob = new BlobRef(backup.ActiveBlob.Partition, ResourceName, PostRestoreBlobId);
        var uploadId = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        var request = new BeginBlobUploadRequest(commandId, nextBlob, uploadId, PostRestoreLength, 0);
        var result = Apply(database, OperationKind.BeginBlobUpload, commandId, request);
        var current = operations.UploadInfo(PrincipalId, new(nextBlob, uploadId));

        await Assert.That(result.Error).IsNull();
        await Assert.That(current!.Status).IsEqualTo(BlobUploadStatus.Active);
        operations.NormalizeRestoredStore();
        await Assert.That(operations.UploadInfo(PrincipalId, new(nextBlob, uploadId))!.Status)
            .IsEqualTo(BlobUploadStatus.Active);

        var writeId = Guid.NewGuid();
        var write = new WriteBlobPartRequest(writeId, nextBlob, uploadId, 0,
            PostRestoreBytes, BlobIntegrity.PartHash(PostRestoreBytes));
        Apply(database, OperationKind.WriteBlobPart, writeId, write).Get<BlobCommitResult<BlobUploadInfo>>();
        var chain = BlobIntegrity.InitialHash(database.Store.Identity.Incarnation,
            nextBlob, uploadId, PostRestoreBytes.Length);
        chain = BlobIntegrity.NextHash(chain, 0, PostRestoreBytes.Length, BlobIntegrity.PartHash(PostRestoreBytes));
        var completeId = Guid.NewGuid();
        var metadata = Apply(database, OperationKind.CompleteBlobUpload, completeId,
            new CompleteBlobUploadRequest(completeId, nextBlob, uploadId, chain))
            .Get<BlobCommitResult<BlobMetadata>>().Value;
        var read = operations.Read(PrincipalId, new(nextBlob, metadata.Revision, 0, PostRestoreBytes.Length));
        await Assert.That(read.Bytes.Span.SequenceEqual(PostRestoreBytes)).IsTrue();
    }

    private static async Task AssertOldUploadOutcomeWasInvalidated(DatabaseEngine database, BlobRestoreBackup backup)
    {
        var oldPartRequest = new WriteBlobPartRequest(backup.ActivePartCommandId, backup.ActiveBlob,
            backup.ActiveUploadId, ActivePartOrdinal, backup.AcceptedPart, BlobIntegrity.PartHash(backup.AcceptedPart));
        var payload = JsonSerializer.Serialize(oldPartRequest, JsonDefaults.Options);
        var operation = new ReplicatedOperation(backup.ActivePartCommandId, OperationKind.WriteBlobPart,
            PrincipalId, TimeProvider.System.GetUtcNow(), payload);
        var failure = database.Apply(operation);
        await Assert.That(failure.Error).IsEqualTo(ErrorCode.TokenInvalidated);

        var oldBeginRequest = new BeginBlobUploadRequest(backup.ActiveBeginCommandId,
            backup.ActiveBlob, backup.ActiveUploadId, AcceptedPartLength + 1L, 0);
        var oldBegin = Apply(database, OperationKind.BeginBlobUpload, backup.ActiveBeginCommandId, oldBeginRequest);
        await Assert.That(oldBegin.Error).IsEqualTo(ErrorCode.TokenInvalidated);
    }

    private static void ConfigureBoundedBlobResource(TestDatabase database)
    {
        var policy = new BlobPolicy
        {
            MaxBlobBytes = AcceptedPartLength + 1L,
            MaxReservedBytes = AcceptedPartLength + PublishedBytes.Length + 1L,
            MaxObjectKeys = ResourceKeyLimit,
            MaxVersions = VersionLimit,
            MaxUploads = ActiveUploadLimit,
            UploadTtlSeconds = UploadTtlSeconds
        };
        var definition = new ResourceDefinition(ResourceName, ResourceKind.BlobStore,
            database.Partition.TransactionDomainId)
        { BlobPolicy = policy };
        database.Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(database.Partition.TenantId, database.Partition.DatabaseId, definition))
            .Get<ResourceDefinition>();
    }

    private static string Chain(TestDatabase database, BlobRef blob, Guid uploadId, byte[] bytes)
    {
        var chain = BlobIntegrity.InitialHash(database.Store.Identity.Incarnation, blob, uploadId, bytes.Length);
        return BlobIntegrity.NextHash(chain, 0, bytes.Length, BlobIntegrity.PartHash(bytes));
    }

    private static OperationResult Apply<T>(DatabaseEngine database, OperationKind kind, Guid commandId, T payload)
    {
        var serialized = JsonSerializer.Serialize(payload, JsonDefaults.Options);
        return database.Apply(new(commandId, kind, PrincipalId, TimeProvider.System.GetUtcNow(), serialized));
    }

    private sealed record BlobRestoreBackup(string BackupDirectory, Guid SourceIncarnation, Guid TargetIncarnation,
        BlobRef PublishedBlob, byte[] PublishedBytes, long PublishedRevision, string PublishedIntegrityHash,
        BlobRef ActiveBlob, Guid ActiveUploadId, Guid ActiveBeginCommandId, Guid ActivePartCommandId, byte[] AcceptedPart);
}
