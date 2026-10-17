namespace KeyLoad.UnitTests.Features.QueryExecution;

internal readonly record struct PartitionQueryCancellationOutcome(
    bool QueryReturned,
    OperationCanceledException? Cancellation,
    long ObservedReadBytes,
    bool CancellationRequested);
