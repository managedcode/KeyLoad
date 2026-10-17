namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record ControlledRetireAspireSeedState(CommandRequest Command, CommitReceipt Receipt,
    ControlledRetireAspireBlobState Blob, DateTimeOffset? RecordedAt = null);

internal sealed record ControlledRetireAspireBlobState(CompleteBlobUploadRequest Request,
    BlobCommitResult<BlobMetadata> Expected);

internal sealed record ControlledRetireAspireHealthyState(CommandRequest Command, CommitReceipt Receipt);
