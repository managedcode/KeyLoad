namespace KeyLoad.UnitTests.Features.EventStreams;

internal sealed record SharedDomainHistory(PartitionRef Partition, ReplicatedOperation Operation, OperationResult Result,
    byte[] OutcomeKey, byte[] OutcomeBytes, byte[] LocatorKey, byte[] LocatorBytes);
