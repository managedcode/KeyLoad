namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

internal static class ScaledRawStorageSnapshotFactory
{
    internal static ScaledRawStorageSnapshot Create(ScaledRawStorageFixtureCore fixture,
        ScaledRawStorageNativeSnapshot native, long peak, long workingSet, long available)
        => new()
        {
            RecordCount = fixture.RecordCount,
            PayloadBytes = fixture.PayloadBytes,
            SeedAttempts = fixture.SeedAttempts,
            SuccessfulSeedWrites = fixture.SuccessfulSeedWrites,
            VerifiedRecords = fixture.VerifiedRecords,
            VerificationPasses = fixture.VerificationPasses,
            NativeReadCalls = fixture.NativeReadCalls,
            RetainedKeyBytes = fixture.RetainedKeyBytes,
            RetainedValueBytes = fixture.RetainedValueBytes,
            RetainedOrderBytes = fixture.RetainedOrderBytes,
            FullValueDigest = fixture.FullValueDigest,
            PermutationDigest = fixture.PermutationDigest,
            ProcessPeakBytes = peak,
            NativeResidentRecords = native.ResidentRecords,
            SeedElapsedTicks = fixture.SeedElapsedTicks,
            VerificationElapsedTicks = fixture.VerificationElapsedTicks,
            StopwatchFrequency = fixture.TimeProvider.TimestampFrequency,
            EffectiveMemoryCapacityBytes = available,
            ProcessMemoryCeilingBytes = fixture.ProcessMemoryCeilingBytes,
            ProcessWorkingSetBytes = workingSet
        };
}
