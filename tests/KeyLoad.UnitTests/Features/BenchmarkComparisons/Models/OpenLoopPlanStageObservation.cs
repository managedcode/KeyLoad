namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed record OpenLoopPlanStageObservation(bool Started, int? ProcessId, int? ExitCode,
    double ExecutionMilliseconds, double SettlementMilliseconds, bool CallerCancelledAtExecutionCompletion,
    bool DeadlineCancelledAtExecutionCompletion, TaskStatus? OutputStatus, TaskStatus? ErrorStatus,
    bool OutputExceededBound, bool ErrorExceededBound, string? StandardOutput, string? StandardError,
    string[] FailureTypes);
