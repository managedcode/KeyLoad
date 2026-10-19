namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

internal static class ScaledRawStorageSnapshotFactory
{
    internal static ScaledRawStorageSnapshot Create(ScaledRawStorageFixtureCore fixture,
        RuntimeScaledRawStorageSnapshot execution, long peak, long workingSet, long available)
        => new()
        {
            RecordCount = fixture.RecordCount,
            PayloadBytes = fixture.PayloadBytes,
            SeedAttempts = fixture.SeedAttempts,
            SuccessfulSeedWrites = fixture.SuccessfulSeedWrites,
            VerifiedRecords = fixture.VerifiedRecords,
            VerificationPasses = fixture.VerificationPasses,
            RuntimeReadCalls = fixture.RuntimeReadCalls,
            RetainedKeyBytes = fixture.RetainedKeyBytes,
            RetainedValueBytes = fixture.RetainedValueBytes,
            RetainedOrderBytes = fixture.RetainedOrderBytes,
            FullValueDigest = fixture.FullValueDigest,
            PermutationDigest = fixture.PermutationDigest,
            ProcessPeakBytes = peak,
            RuntimeResidentRecords = execution.ResidentRecords,
            SeedElapsedTicks = fixture.SeedElapsedTicks,
            VerificationElapsedTicks = fixture.VerificationElapsedTicks,
            StopwatchFrequency = fixture.TimeProvider.TimestampFrequency,
            EffectiveMemoryCapacityBytes = available,
            ProcessMemoryCeilingBytes = fixture.ProcessMemoryCeilingBytes,
            ProcessWorkingSetBytes = workingSet
        };
}
