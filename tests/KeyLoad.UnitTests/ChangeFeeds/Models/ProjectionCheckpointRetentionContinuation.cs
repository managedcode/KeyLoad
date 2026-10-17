namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal sealed record ProjectionCheckpointRetentionContinuation(ProjectionCheckpointRetentionState Original,
    OperationResult ProbeFailure, OperationResult Completed, ReplicatedOperation Filtered, OperationResult Advanced,
    ReplicatedOperation Purge, OperationResult Purged, ReplicatedOperation Healthy, OperationResult Produced,
    string[] Image, long Position);
