
namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed record QueueViewLeaseIsolationHistory(ReplicatedOperation Operation, OperationResult Result,
    ReplicaEntry Entry, byte[] OutcomeKey, byte[] Outcome, byte[] LocatorKey, byte[]? Locator);
