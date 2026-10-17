using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed record TestDatabaseConcurrentCloseSeed(PartitionRef Partition, ReplicatedOperation Original,
    OperationResult Result, CommitReceipt ExpectedReceipt, StoreIdentity Identity, long LocalPosition, string[] Canonical);
