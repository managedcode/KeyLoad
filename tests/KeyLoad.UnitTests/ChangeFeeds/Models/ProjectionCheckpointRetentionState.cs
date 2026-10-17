namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal sealed record ProjectionCheckpointRetentionState(ProjectionConsumerIdentityState Source,
    ReplicatedOperation Pinned, OperationResult PinFailure, ReplicatedOperation Probe, string[] DirtyImage, long DirtyPosition);
