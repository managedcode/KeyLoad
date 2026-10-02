namespace KeyLoad.UnitTests.Features.BlobStorage;

internal sealed class BlobConfigurationCounterTests
{
    private const string ResourceName = "blobs";
    private const string FirstBlobId = "counter-first";
    private const string SecondBlobId = "counter-second";
    private const int ObjectKeyLimit = 1;
    private const int VersionLimit = 2;
    private const int UploadLimit = 1;
    private const int MinimumTtlSeconds = 60;
    private const long ByteLimit = 10;
    private const long InitialReservation = 6;
    private const long ReplacementReservation = 4;

    [Test]
    public async Task AcBlob005RepeatedConfigurationPreservesPersistedBlobCounters()
    {
        using var database = new TestDatabase();
        var policy = new BlobPolicy
        {
            MaxBlobBytes = ByteLimit,
            MaxReservedBytes = ByteLimit,
            MaxObjectKeys = ObjectKeyLimit,
            MaxVersions = VersionLimit,
            MaxUploads = UploadLimit,
            UploadTtlSeconds = MinimumTtlSeconds
        };
        var definition = new ResourceDefinition(ResourceName, ResourceKind.BlobStore,
            database.Partition.TransactionDomainId)
        { BlobPolicy = policy };
        Configure(database, definition);

        var firstBlob = new BlobRef(database.Partition, ResourceName, FirstBlobId);
        var firstUpload = Guid.NewGuid();
        Begin(database, firstBlob, InitialReservation, firstUpload).Get<BlobCommitResult<BlobUploadInfo>>();
        Configure(database, definition);

        var secondBlob = new BlobRef(database.Partition, ResourceName, SecondBlobId);
        var objectLimit = Begin(database, secondBlob, 1, Guid.NewGuid());
        var uploadLimit = Begin(database, firstBlob, 1, Guid.NewGuid());
        await Assert.That(objectLimit.Error).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(uploadLimit.Error).IsEqualTo(ErrorCode.ResourceExhausted);

        Abort(database, firstBlob, firstUpload);
        var replacementUpload = Guid.NewGuid();
        Begin(database, firstBlob, ReplacementReservation, replacementUpload)
            .Get<BlobCommitResult<BlobUploadInfo>>();
        Abort(database, firstBlob, replacementUpload);

        var exhaustedVersion = Begin(database, firstBlob, 1, Guid.NewGuid());
        await Assert.That(exhaustedVersion.Error).IsEqualTo(ErrorCode.ResourceExhausted);
    }

    private static void Configure(TestDatabase database, ResourceDefinition definition)
    {
        database.Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(database.Partition.TenantId, database.Partition.DatabaseId, definition))
            .Get<ResourceDefinition>();
    }

    private static OperationResult Begin(TestDatabase database, BlobRef blob, long length, Guid uploadId)
    {
        var commandId = Guid.NewGuid();
        var request = new BeginBlobUploadRequest(commandId, blob, uploadId, length, 0);
        return database.Submit(OperationKind.BeginBlobUpload, request, id: commandId);
    }

    private static void Abort(TestDatabase database, BlobRef blob, Guid uploadId)
    {
        var commandId = Guid.NewGuid();
        var request = new AbortBlobUploadRequest(commandId, blob, uploadId);
        database.Submit(OperationKind.AbortBlobUpload, request, id: commandId)
            .Get<BlobCommitResult<BlobUploadInfo>>();
    }
}
