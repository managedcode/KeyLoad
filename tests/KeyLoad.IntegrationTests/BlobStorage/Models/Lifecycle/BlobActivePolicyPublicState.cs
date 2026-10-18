using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal sealed class BlobActivePolicyPublicState(BlobRef blob, McpPersistedIdentity identity)
{
    internal BlobRef Blob { get; } = blob;
    internal McpPersistedIdentity Identity { get; } = identity;
    internal byte[] Bytes { get; } = [11, 23, 37, 53];
    internal BeginBlobUploadRequest Begin { get; } = new(Guid.NewGuid(), blob, Guid.NewGuid(), 4L, 0L);
    internal BlobCommitResult<BlobUploadInfo> Begun { get; set; } = null!;
    internal BlobUploadInfo Current { get; set; } = null!;
    internal WriteBlobPartRequest DeniedPart { get; set; } = null!;
    internal CompleteBlobUploadRequest DeniedComplete { get; set; } = null!;
    internal AbortBlobUploadRequest DeniedAbort { get; set; } = null!;
    internal WriteBlobPartRequest Part { get; set; } = null!;
    internal BlobCommitResult<BlobUploadInfo> Written { get; set; } = null!;
    internal CompleteBlobUploadRequest Complete { get; set; } = null!;
    internal BlobCommitResult<BlobMetadata>? Completed
    {
        get; set;
    }
}
