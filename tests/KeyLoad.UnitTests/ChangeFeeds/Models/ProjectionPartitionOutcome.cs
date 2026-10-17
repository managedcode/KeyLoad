namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal sealed record ProjectionPartitionOutcome(ReplicatedOperation Operation, OperationResult Result);
