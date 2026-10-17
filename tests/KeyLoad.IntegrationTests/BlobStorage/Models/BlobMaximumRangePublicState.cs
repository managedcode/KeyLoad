namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal sealed record BlobMaximumRangePublicState(BeginBlobUploadRequest Begin,
    BlobCommitResult<BlobUploadInfo> Begun, WriteBlobPartRequest[] Parts,
    BlobCommitResult<BlobUploadInfo>[] Written, CompleteBlobUploadRequest Complete,
    BlobCommitResult<BlobMetadata> Completed, AtomicPartitionPlacementResolution Placement)
{
    internal BlobUploadInfo CurrentUpload => Written[^1].Value with { Status = BlobUploadStatus.Complete };
}
