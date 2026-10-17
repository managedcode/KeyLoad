namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record ControlledRetireAspireBlobState(CompleteBlobUploadRequest Request,
    BlobCommitResult<BlobMetadata> Expected);
