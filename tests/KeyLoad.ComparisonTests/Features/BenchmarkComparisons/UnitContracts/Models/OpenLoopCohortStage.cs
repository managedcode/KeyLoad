namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal enum OpenLoopCohortStage
{
    SeedFailedCohort,
    RejectCorruptIntake,
    RejectExtraArchive,
    AggregateRecoveredCohort,
    RejectCreateOnlyReuse
}
