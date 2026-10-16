namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal sealed record FunctionalGateBlobVersion(BeginBlobUploadRequest Begin, byte[] Bytes,
    CompleteBlobUploadRequest Complete, BlobCommitResult<BlobMetadata> Published);
