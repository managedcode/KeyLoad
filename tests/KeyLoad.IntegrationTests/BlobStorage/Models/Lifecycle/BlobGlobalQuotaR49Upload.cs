namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal sealed class BlobGlobalQuotaR49Upload(BlobRef blob, long length)
{
    internal BlobRef Blob { get; } = blob;
    internal BeginBlobUploadRequest Begin { get; } = new(Guid.NewGuid(), blob, Guid.NewGuid(), length, 0L);
    internal BeginBlobUploadRequest? AcceptedBegin
    {
        get; set;
    }
    internal BlobCommitResult<BlobUploadInfo>? Begun
    {
        get; set;
    }
    internal List<WriteBlobPartRequest> Parts { get; } = [];
    internal List<BlobCommitResult<BlobUploadInfo>> Written { get; } = [];
    internal CompleteBlobUploadRequest? Complete
    {
        get; set;
    }
    internal BlobCommitResult<BlobMetadata>? Completed
    {
        get; set;
    }
    internal AbortBlobUploadRequest? Abort
    {
        get; set;
    }
    internal BlobCommitResult<BlobUploadInfo>? Aborted
    {
        get; set;
    }
    internal ReclaimBlobRequest? Reclaim
    {
        get; set;
    }
    internal BlobCommitResult<BlobReclaimResult>? Reclaimed
    {
        get; set;
    }
    internal BlobUploadInfo? Progress
    {
        get; set;
    }
    internal BlobMetadata? Metadata
    {
        get; set;
    }
    internal bool Removed
    {
        get; set;
    }
}
