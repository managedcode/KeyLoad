namespace KeyLoad.RecoveryTests.Features.EventStreams;

internal sealed record SharedDomainRecoveryHistory(ReplicatedOperation Operation, OperationResult Result, PartitionRef Partition,
    byte[] OutcomeKey, byte[] OutcomeBytes, byte[] LocatorKey, byte[] LocatorBytes);
