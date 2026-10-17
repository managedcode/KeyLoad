namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal sealed record ProjectionConcurrentCommitState(ProjectionConsumerIdentityState Source,
    ReplicatedOperation First, OperationResult FirstResult, ReplicatedOperation Second, OperationResult SecondResult,
    bool WinnerIsFirst, ReplicatedOperation Filtered, OperationResult Advanced, string[] Image, long Position);
