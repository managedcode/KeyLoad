namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal sealed record BlobArchiveActivePublicVersion(BeginBlobUploadRequest Begin,
    BlobCommitResult<BlobUploadInfo> Begun, WriteBlobPartRequest[] Parts,
    BlobCommitResult<BlobUploadInfo>[] Written, CompleteBlobUploadRequest? Complete,
    BlobCommitResult<BlobMetadata>? Completed);
