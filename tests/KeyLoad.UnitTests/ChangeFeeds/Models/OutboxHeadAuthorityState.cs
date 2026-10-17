namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal sealed record OutboxHeadAuthorityState(PartitionRef Partition, ReplicatedOperation Seed, CommitReceipt Receipt, OperationResult SeedResult,
    ReplicatedOperation QuotaWrite, OperationResult QuotaFailure, ReplicatedOperation Purge,
    byte[] Key, byte[] Original, long StoredBytes, long FirstBytes);
