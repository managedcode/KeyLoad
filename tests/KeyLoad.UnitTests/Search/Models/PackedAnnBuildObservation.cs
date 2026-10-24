namespace KeyLoad.UnitTests.Search;

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
