namespace KeyLoad.UnitTests.Features.BlobStorage;

internal sealed class BlobStorageCompatibilityTests
{
    private const int ExistingCollectionResourceId = 0;
    private const int ExistingTimeSeriesResourceId = 5;
    private const int BlobStoreResourceId = 6;
    private const int FirstBlobOperationId = 19;
    private const int LastBlobOperationId = 24;
    private const long BlobReadCapability = 1L << 30;
    private const long BlobWriteCapability = 1L << 31;
    private const long BlobDeleteCapability = 1L << 32;
    private const long BlobManageCapability = 1L << 33;
    private const long AllCurrentCapabilities = (1L << 34) - 1;
    private const long DefaultMaxBlobBytes = 67_108_864;
    private const long DefaultMaxReservedBytes = 268_435_456;
    private const int DefaultMaxObjectKeys = 4_096;
    private const int DefaultMaxVersions = 8_192;
    private const int DefaultMaxUploads = 128;
    private const int DefaultUploadTtlSeconds = 3_600;
    private const string LegacyResourceJson = "{\"name\":\"orders\",\"kind\":\"Collection\",\"transactionDomainId\":\"orders\","
        + "\"indexes\":[],\"fieldPolicies\":[],\"headerPolicies\":[],"
        + "\"queuePolicy\":{\"maxAttempts\":5,\"maxStoredMessages\":100000,\"maxStoredBytes\":1073741824,"
        + "\"maxInFlightMessages\":1000,\"maxInFlightBytes\":67108864,\"maxLeaseSeconds\":300,"
        + "\"retryBaseMilliseconds\":1000,\"retryMaxMilliseconds\":300000},"
        + "\"eventRetention\":{\"maxEvents\":100000,\"maxBytes\":1073741824},"
        + "\"authority\":\"Document\",\"schemaVersion\":1,\"paused\":false}";

    [Test]
    public async Task AcBlob007AppendsBlobEnumsAndCapabilitiesWithoutRenumberingExistingValues()
    {
        await Assert.That(Numeric(ResourceKind.Collection)).IsEqualTo(ExistingCollectionResourceId);
        await Assert.That(Numeric(ResourceKind.TimeSeries)).IsEqualTo(ExistingTimeSeriesResourceId);
        await Assert.That(Numeric(ResourceKind.BlobStore)).IsEqualTo(BlobStoreResourceId);
        await Assert.That(Numeric(OperationKind.BeginBlobUpload)).IsEqualTo(FirstBlobOperationId);
        await Assert.That(Numeric(OperationKind.ReclaimBlob)).IsEqualTo(LastBlobOperationId);
        await Assert.That(Numeric(Capability.BlobRead)).IsEqualTo(BlobReadCapability);
        await Assert.That(Numeric(Capability.BlobWrite)).IsEqualTo(BlobWriteCapability);
        await Assert.That(Numeric(Capability.BlobDelete)).IsEqualTo(BlobDeleteCapability);
        await Assert.That(Numeric(Capability.BlobManage)).IsEqualTo(BlobManageCapability);
        await Assert.That(Numeric(Capability.All)).IsEqualTo(AllCurrentCapabilities);
        await Assert.That(Numeric(BlobUploadStatus.Active)).IsEqualTo(0);
        await Assert.That(Numeric(BlobUploadStatus.Complete)).IsEqualTo(1);
        await Assert.That(Numeric(BlobUploadStatus.Aborted)).IsEqualTo(2);
        await Assert.That(Numeric(BlobUploadStatus.Expired)).IsEqualTo(3);
    }

    private static long Numeric<TEnum>(TEnum value) where TEnum : struct, Enum =>
        Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);

    [Test]
    public async Task AcBlob007ResourceWithoutBlobPolicyRetainsItsCanonicalJsonBytes()
    {
        var resource = new ResourceDefinition("orders", ResourceKind.Collection, "orders");
        var policy = new BlobPolicy();

        await Assert.That(JsonDefaults.Serialize(resource).SequenceEqual(
            System.Text.Encoding.UTF8.GetBytes(LegacyResourceJson))).IsTrue();
        await Assert.That(resource.BlobPolicy).IsNull();
        await Assert.That(policy.MaxBlobBytes).IsEqualTo(DefaultMaxBlobBytes);
        await Assert.That(policy.MaxReservedBytes).IsEqualTo(DefaultMaxReservedBytes);
        await Assert.That(policy.MaxObjectKeys).IsEqualTo(DefaultMaxObjectKeys);
        await Assert.That(policy.MaxVersions).IsEqualTo(DefaultMaxVersions);
        await Assert.That(policy.MaxUploads).IsEqualTo(DefaultMaxUploads);
        await Assert.That(policy.UploadTtlSeconds).IsEqualTo(DefaultUploadTtlSeconds);
    }
}
