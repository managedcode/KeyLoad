namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal sealed record FunctionalGateColdBlob(BeginBlobUploadRequest Begin, BlobCommitResult<BlobUploadInfo> Begun,
    FunctionalGateColdPart[] Parts, CompleteBlobUploadRequest Complete,
    BlobCommitResult<BlobMetadata> Published, byte[] Bytes);
