namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

// Borrowed exact original operation evidence; never public transport or caller authority.
internal sealed record PartitionMovementPublicParentRf3BlobOriginals(
    BeginBlobUploadRequest Begin,
    BlobCommitResult<BlobUploadInfo> Begun,
    WriteBlobPartRequest First,
    BlobCommitResult<BlobUploadInfo> FirstReceipt,
    WriteBlobPartRequest Tail,
    BlobCommitResult<BlobUploadInfo> TailReceipt,
    CompleteBlobUploadRequest Complete,
    BlobCommitResult<BlobMetadata> Completed,
    WriteBlobPartRequest? FailedPart = null);
