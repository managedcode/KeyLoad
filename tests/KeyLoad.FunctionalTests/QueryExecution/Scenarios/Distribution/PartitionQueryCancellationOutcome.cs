namespace KeyLoad.FunctionalTests.QueryExecution;

internal readonly record struct PartitionQueryCancellationOutcome(
    bool QueryReturned,
    OperationCanceledException? Cancellation,
    long ObservedReadBytes,
    bool CancellationRequested);
