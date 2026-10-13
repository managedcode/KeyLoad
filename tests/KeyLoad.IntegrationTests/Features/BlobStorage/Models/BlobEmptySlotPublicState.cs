namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal sealed class BlobEmptySlotPublicState(BlobRef blob)
{
    internal BlobRef Blob { get; } = blob;
    internal BeginBlobUploadRequest First { get; } = new(Guid.NewGuid(), blob, Guid.NewGuid(), 0L, 0L);
    internal BeginBlobUploadRequest InitialRefused { get; } = new(Guid.NewGuid(), blob, Guid.NewGuid(), 0L, 0L);
    internal BeginBlobUploadRequest RetainedRefused { get; } = new(Guid.NewGuid(), blob, Guid.NewGuid(), 0L, 0L);
    internal BlobCommitResult<BlobUploadInfo> Begun { get; set; } = null!;
    internal AbortBlobUploadRequest Abort { get; set; } = null!;
    internal ReclaimBlobRequest Reclaim { get; set; } = null!;
    internal BlobCommitResult<BlobUploadInfo> Aborted { get; set; } = null!;
    internal BlobCommitResult<BlobReclaimResult> Reclaimed { get; set; } = null!;
    internal BeginBlobUploadRequest Healthy { get; set; } = null!;
    internal BlobCommitResult<BlobUploadInfo> HealthyBegun { get; set; } = null!;
    internal CompleteBlobUploadRequest Complete { get; set; } = null!;
    internal BlobCommitResult<BlobMetadata>? Published { get; set; }
    internal BlobUploadInfo? HealthyCurrent { get; set; }
    internal BlobUploadInfo? OriginalCurrent { get; set; }
}
