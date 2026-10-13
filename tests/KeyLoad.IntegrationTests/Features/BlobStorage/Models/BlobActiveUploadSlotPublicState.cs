namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal sealed class BlobActiveUploadSlotPublicState(BlobRef blob)
{
    internal BlobRef Blob { get; } = blob;
    internal BeginBlobUploadRequest Original { get; } = new(Guid.NewGuid(), blob, Guid.NewGuid(), 0L, 0L);
    internal BeginBlobUploadRequest Admitted { get; set; } = null!;
    internal BlobCommitResult<BlobUploadInfo> Begun { get; set; } = null!;
    internal BlobUploadInfo? Current { get; set; }
    internal AbortBlobUploadRequest Abort { get; set; } = null!;
    internal BlobCommitResult<BlobUploadInfo> Aborted { get; set; } = null!;
    internal ReclaimBlobRequest Reclaim { get; set; } = null!;
    internal BlobCommitResult<BlobReclaimResult> Reclaimed { get; set; } = null!;
    internal CompleteBlobUploadRequest Complete { get; set; } = null!;
    internal BlobCommitResult<BlobMetadata>? Published { get; set; }
}
