using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Security;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal sealed class BlobRestorePageNormalizationTests
{
    private const string DirectoryPrefix = "keyload-blob-page-restore-";
    private const string SourceDirectoryName = "source";
    private const string BackupDirectoryName = "backup";
    private const string RestoredDirectoryName = "restored";
    private const string TenantId = "tenant";
    private const string DatabaseId = "database";
    private const string DomainId = "orders";
    private const string PartitionKey = "customer-1";
    private const string PrincipalId = "root";
    private const string ResourceName = "blobs";
    private const string BlobIdPrefix = "page-upload-";
    private const string QuotaSpace = "blob-quota-v1";
    private const string GlobalSpace = "blob-global-v1";
    private const string ReservedBytesProperty = "reservedBytes";
    private const string ObjectKeysProperty = "objectKeys";
    private const string VersionsProperty = "versions";
    private const string UploadsProperty = "uploads";
    private const string GuidFormat = "N";
    private const int RecordCount = BlobLimits.MaxReclaimParts + 1;
    private const int TtlSeconds = 3_600;
    private const long ReservedLength = 1;

    [Test]
    public async Task AcBlob004NormalizationReopensAcrossMoreThanOneStateAndHeadPage()
    {
        var root = Path.Combine(Path.GetTempPath(), DirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));
        Directory.CreateDirectory(root);
        try
        {
            var backup = CreateBackup(root);
            var targetIncarnation = Guid.NewGuid();
            var restoredPath = Path.Combine(root, RestoredDirectoryName);
            var restoredIdentity = ZoneTreeStore.Restore(backup.BackupPath, restoredPath, targetIncarnation);
            using var restoredStore = new ZoneTreeStore(new(restoredPath));
            var database = new DatabaseEngine(restoredStore, new AuthorizationPolicy());
            var operations = new BlobStorageOperations(database);

            await Assert.That(restoredIdentity.Incarnation).IsEqualTo(targetIncarnation);
            await Assert.That(restoredIdentity.Incarnation).IsNotEqualTo(backup.SourceIncarnation);
            operations.NormalizeRestoredStore();
            await AssertBoundaryUploadsAreAborted(operations);
            await AssertQuotaCounters(restoredStore);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static BlobPageBackup CreateBackup(string root)
    {
        var sourcePath = Path.Combine(root, SourceDirectoryName);
        var backupPath = Path.Combine(root, BackupDirectoryName);
        using var database = new TestDatabase(directory: sourcePath);
        ConfigureBoundedResource(database);
        for (var index = 0; index < RecordCount; index++)
        {
            BeginActiveUpload(database, index);
        }
        database.Store.CreateBackup(backupPath);
        return new(backupPath, database.Store.Identity.Incarnation);
    }

    private static void ConfigureBoundedResource(TestDatabase database)
    {
        var policy = new BlobPolicy
        {
            MaxBlobBytes = ReservedLength,
            MaxReservedBytes = RecordCount,
            MaxObjectKeys = RecordCount,
            MaxVersions = RecordCount,
            MaxUploads = RecordCount,
            UploadTtlSeconds = TtlSeconds
        };
        var definition = new ResourceDefinition(ResourceName, ResourceKind.BlobStore,
            database.Partition.TransactionDomainId)
        { BlobPolicy = policy };
        database.Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(database.Partition.TenantId, database.Partition.DatabaseId, definition))
            .Get<ResourceDefinition>();
    }

    private static void BeginActiveUpload(TestDatabase database, int index)
    {
        var blob = BlobStorageTestSupport.Blob(database, BlobId(index));
        BlobStorageTestSupport.Begin(database, blob, UploadId(index), ReservedLength, 0);
    }

    private static async Task AssertBoundaryUploadsAreAborted(BlobStorageOperations operations)
    {
        var boundaryIndexes = new[] { 0, BlobLimits.MaxReclaimParts - 1, BlobLimits.MaxReclaimParts };
        foreach (var index in boundaryIndexes)
        {
            var upload = operations.UploadInfo(PrincipalId, new(Blob(index), UploadId(index)));
            await Assert.That(upload).IsNotNull();
            await Assert.That(upload!.Status).IsEqualTo(BlobUploadStatus.Aborted);
            await Assert.That(upload.StoredBytes).IsEqualTo(0L);
        }
    }

    private static async Task AssertQuotaCounters(ZoneTreeStore store)
    {
        var resourceQuotaKey = KeyCodec.Encode(QuotaSpace, TenantId, DatabaseId, DomainId, ResourceName);
        var globalQuotaKey = KeyCodec.Encode(GlobalSpace);
        var resourceQuota = ParseObject(store.Read(view => view.ReadOwnedValue(resourceQuotaKey)));
        var globalQuota = ParseObject(store.Read(view => view.ReadOwnedValue(globalQuotaKey)));

        await AssertQuota(resourceQuota);
        await AssertQuota(globalQuota);
    }

    private static async Task AssertQuota(JsonDocument quota)
    {
        using (quota)
        {
            var root = quota.RootElement;
            await Assert.That(root.GetProperty(ReservedBytesProperty).GetInt64()).IsEqualTo(0L);
            await Assert.That(root.GetProperty(ObjectKeysProperty).GetInt32()).IsEqualTo(RecordCount);
            await Assert.That(root.GetProperty(VersionsProperty).GetInt32()).IsEqualTo(RecordCount);
            await Assert.That(root.GetProperty(UploadsProperty).GetInt32()).IsEqualTo(0);
        }
    }

    private static JsonDocument ParseObject(byte[]? bytes) => JsonDocument.Parse(bytes
        ?? throw new InvalidOperationException("Expected a persisted blob quota record."));

    private static BlobRef Blob(int index) =>
        new(new(TenantId, DatabaseId, DomainId, PartitionKey), ResourceName, BlobId(index));

    private static string BlobId(int index) => BlobIdPrefix + index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture);

    private static Guid UploadId(int index) => Guid.ParseExact(
        (index + 1).ToString("x32", System.Globalization.CultureInfo.InvariantCulture), GuidFormat);

    private sealed record BlobPageBackup(string BackupPath, Guid SourceIncarnation);
}
