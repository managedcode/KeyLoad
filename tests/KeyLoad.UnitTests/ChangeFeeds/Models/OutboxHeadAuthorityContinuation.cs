namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal sealed record OutboxHeadAuthorityContinuation(ReplicatedOperation Healthy,
    CommitReceipt Receipt, OperationResult HealthyResult, OperationResult Purged, string[] Image, long Position);
