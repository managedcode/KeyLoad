namespace KeyLoad.UnitTests.Features.Search;

internal enum PackedAnnBuildScenario
{
    AdaptiveSelectivity,
    AdaptiveFallback,
    WideSearchSetup,
    RecallMetric,
    FilteredFallback
}

internal enum PackedAnnBuildOutcome
{
    Success,
    BudgetExceeded,
    Canceled,
    Failure
}

internal readonly record struct PackedAnnBuildObservation(
    PackedAnnBuildScenario Scenario,
    int RecordCount,
    int Dimension,
    DistanceMetric Metric,
    int Connections,
    int EfConstruction,
    int EfSearch,
    int MaxLevel,
    int ExactThreshold,
    int MaxRecords,
    long MaxIndexBytes,
    long MaxScratchBytes,
    ulong Seed,
    PackedAnnBuildOutcome Outcome,
    ErrorCode? FailureCode,
    long StopwatchFrequency,
    long ElapsedStopwatchTicks,
    long AllocatedBytes,
    long WorkUnits,
    long DistanceEvaluations,
    long EdgeVisits);
