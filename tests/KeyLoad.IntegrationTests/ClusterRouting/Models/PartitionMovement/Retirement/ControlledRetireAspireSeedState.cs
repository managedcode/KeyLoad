namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record ControlledRetireAspireSeedState(CommandRequest Command, CommitReceipt Receipt,
    ControlledRetireAspireBlobState Blob, DateTimeOffset? RecordedAt = null);
