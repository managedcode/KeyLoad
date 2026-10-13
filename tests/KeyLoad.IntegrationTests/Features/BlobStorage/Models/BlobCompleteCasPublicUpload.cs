namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal sealed class BlobCompleteCasPublicUpload(BlobRef blob, long expectedRevision, byte[] bytes, string sha256)
{
    internal byte[] Bytes { get; } = bytes;
    internal string Sha256 { get; } = sha256;
    internal BeginBlobUploadRequest Begin { get; } = new(Guid.NewGuid(), blob, Guid.NewGuid(), bytes.Length, expectedRevision);
    internal BlobCommitResult<BlobUploadInfo> Begun { get; set; } = null!;
    internal WriteBlobPartRequest Part { get; set; } = null!;
    internal BlobCommitResult<BlobUploadInfo> Written { get; set; } = null!;
    internal CompleteBlobUploadRequest Complete { get; set; } = null!;
    internal CompleteBlobUploadRequest FreshRefused { get; set; } = null!;
    internal BlobCommitResult<BlobMetadata>? Completed { get; set; }
    internal BlobUploadInfo? Current { get; set; }
    internal AbortBlobUploadRequest Abort { get; set; } = null!;
    internal BlobCommitResult<BlobUploadInfo> Aborted { get; set; } = null!;
    internal ReclaimBlobRequest Reclaim { get; set; } = null!;
    internal BlobCommitResult<BlobReclaimResult> Reclaimed { get; set; } = null!;
}
