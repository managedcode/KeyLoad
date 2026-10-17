namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal sealed record ProjectionConsumerIdentityContinuation(OperationResult Completed,
    ReplicatedOperation Filtered, OperationResult Advanced, string[] Image, long Position);
