namespace KeyLoad.UnitTests.Features.BlobStorage;

internal static class BlobCurrentFormatIdentities
{
    private const int ActiveStatus = 0;
    private const int CompleteStatus = 1;
    private const int AbortedStatus = 2;
    private const int ExpiredStatus = 3;
    private const int ExistingCollectionResourceId = 0;
    private const int ExistingTimeSeriesResourceId = 5;
    private const int BlobStoreResourceId = 6;
    private const int FirstBlobOperationId = 19;
    private const int LastBlobOperationId = 24;
    private const long BlobReadCapability = 1L << 30;
    private const long BlobWriteCapability = 1L << 31;
    private const long BlobDeleteCapability = 1L << 32;
    private const long BlobManageCapability = 1L << 33;
    private const long SeriesManageCapability = 1L << 34;
    private const long EventsReplayCapability = 1L << 35;
    private const long EventsSnapshotsManageCapability = 1L << 36;
    private const long SchedulerManageCapability = 1L << 37;
    private const long AllCurrentCapabilities = (1L << 38) - 1;
    internal static async Task StableIdentitiesAsync()
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
        await Assert.That(Numeric(Capability.SeriesManage)).IsEqualTo(SeriesManageCapability);
        await Assert.That(Numeric(Capability.EventsReplay)).IsEqualTo(EventsReplayCapability);
        await Assert.That(Numeric(Capability.EventsSnapshotsManage)).IsEqualTo(EventsSnapshotsManageCapability);
        await Assert.That(Numeric(Capability.SchedulerManage)).IsEqualTo(SchedulerManageCapability);
        await Assert.That(Numeric(Capability.All)).IsEqualTo(AllCurrentCapabilities);
        await Assert.That(Numeric(BlobUploadStatus.Active)).IsEqualTo(ActiveStatus);
        await Assert.That(Numeric(BlobUploadStatus.Complete)).IsEqualTo(CompleteStatus);
        await Assert.That(Numeric(BlobUploadStatus.Aborted)).IsEqualTo(AbortedStatus);
        await Assert.That(Numeric(BlobUploadStatus.Expired)).IsEqualTo(ExpiredStatus);
    }

    private static long Numeric<TEnum>(TEnum value) where TEnum : struct, Enum =>
        Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);

}
