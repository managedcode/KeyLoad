using KeyLoad.Core.Features.BlobStorage;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal sealed class BlobQuotaIsolationTests
{
    private const string PrincipalId = "root";
    private const string FirstResource = "blobs";
    private const string SecondResource = "other-blobs";
    private const string FirstPartitionKey = "customer-1";
    private const string SecondPartitionKey = "customer-2";
    private const long SmallLimit = 3;
    private const long FirstGlobalReservation = 600_000_000;
    private const long RejectedGlobalReservation = 500_000_000;
    private const int SmallIdentityLimit = 4;
    private const int SmallVersionLimit = 4;
    private const int SmallUploadLimit = 4;
    private const int MinimumTtlSeconds = 60;

    [Test]
    public async Task AcBlob005ResourceQuotaAggregatesPartitionKeysButSeparatesResources()
    {
        using var db = new TestDatabase();
        var bounded = Policy(SmallLimit, SmallLimit);
        Configure(db, FirstResource, bounded);
        Configure(db, SecondResource, bounded);
        var first = new BlobRef(db.Partition with { PartitionKey = FirstPartitionKey }, FirstResource, "first");
        var sameResourceOtherPartition = new BlobRef(
            db.Partition with { PartitionKey = SecondPartitionKey }, FirstResource, "second");
        var separateResource = sameResourceOtherPartition with { Resource = SecondResource };
        Begin(db, first, SmallLimit, Guid.NewGuid()).Get<BlobCommitResult<BlobUploadInfo>>();

        var sameResourceFailure = Begin(db, sameResourceOtherPartition, 1, Guid.NewGuid());
        await Assert.That(sameResourceFailure.Error).IsEqualTo(ErrorCode.ResourceExhausted);
        Begin(db, separateResource, SmallLimit, Guid.NewGuid()).Get<BlobCommitResult<BlobUploadInfo>>();
    }

    [Test]
    public async Task AcBlob005GlobalReservationRejectsOverflowAcrossBlobResourcesWithoutStagingUpload()
    {
        using var db = new TestDatabase();
        Configure(db, FirstResource, Policy(FirstGlobalReservation, FirstGlobalReservation));
        Configure(db, SecondResource, Policy(RejectedGlobalReservation, RejectedGlobalReservation));
        var firstBlob = new BlobRef(db.Partition, FirstResource, "large-first-reservation");
        var secondBlob = new BlobRef(db.Partition, SecondResource, "large-second-reservation");
        Begin(db, firstBlob, FirstGlobalReservation, Guid.NewGuid()).Get<BlobCommitResult<BlobUploadInfo>>();

        var rejectedUploadId = Guid.NewGuid();
        var rejected = Begin(db, secondBlob, RejectedGlobalReservation, rejectedUploadId);
        await Assert.That(rejected.Error).IsEqualTo(ErrorCode.ResourceExhausted);
        var operations = new BlobStorageOperations(db.Database);
        await Assert.That(operations.UploadInfo(PrincipalId, new(secondBlob, rejectedUploadId))).IsNull();
    }

    private static BlobPolicy Policy(long maxBlobBytes, long maxReservedBytes) => new()
    {
        MaxBlobBytes = maxBlobBytes,
        MaxReservedBytes = maxReservedBytes,
        MaxObjectKeys = SmallIdentityLimit,
        MaxVersions = SmallVersionLimit,
        MaxUploads = SmallUploadLimit,
        UploadTtlSeconds = MinimumTtlSeconds
    };

    private static void Configure(TestDatabase database, string resource, BlobPolicy policy)
    {
        var definition = new ResourceDefinition(resource, ResourceKind.BlobStore,
            database.Partition.TransactionDomainId)
        { BlobPolicy = policy };
        database.Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(database.Partition.TenantId, database.Partition.DatabaseId, definition))
            .Get<ResourceDefinition>();
    }

    private static OperationResult Begin(TestDatabase database, BlobRef blob, long length, Guid uploadId)
    {
        var id = Guid.NewGuid();
        return database.Submit(OperationKind.BeginBlobUpload,
            new BeginBlobUploadRequest(id, blob, uploadId, length, 0), id: id);
    }
}
