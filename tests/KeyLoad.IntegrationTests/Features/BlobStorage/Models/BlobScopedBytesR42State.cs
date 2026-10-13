namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal sealed class BlobScopedBytesR42State(int index, BlobRef blob, Guid upload)
{
    internal int Index { get; } = index;
    internal BlobRef Blob { get; } = blob;
    internal BeginBlobUploadRequest Begin { get; } = new(Guid.NewGuid(), blob, upload, BlobScopedBytesR42Corpus.Length, 0L);
    internal AtomicPartitionPlacementResolution Placement { get; set; } = null!;
    internal BlobCommitResult<BlobUploadInfo> Begun { get; set; } = null!;
    internal WriteBlobPartRequest[] Parts { get; } = new WriteBlobPartRequest[2];
    internal BlobCommitResult<BlobUploadInfo>[] Written { get; } = new BlobCommitResult<BlobUploadInfo>[2];
    internal BlobUploadInfo? Current { get; set; }
    internal CompleteBlobUploadRequest Complete { get; set; } = null!;
    internal BlobCommitResult<BlobMetadata>? Published { get; set; }
    internal AbortBlobUploadRequest Abort { get; set; } = null!;
    internal BlobCommitResult<BlobUploadInfo> Aborted { get; set; } = null!;
    internal ReclaimBlobRequest[] Reclaims { get; } = new ReclaimBlobRequest[2];
    internal BlobCommitResult<BlobReclaimResult>[] Reclaimed { get; } = new BlobCommitResult<BlobReclaimResult>[2];
    internal Dictionary<Guid, object> Refusals { get; } = [];
}
