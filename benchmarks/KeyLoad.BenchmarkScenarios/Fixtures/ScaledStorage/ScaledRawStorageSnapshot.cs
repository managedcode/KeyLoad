namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

internal sealed record ScaledRawStorageSnapshot
{
    internal required int RecordCount
    {
        get; init;
    }
    internal required int PayloadBytes
    {
        get; init;
    }
    internal required long SeedAttempts
    {
        get; init;
    }
    internal required long SuccessfulSeedWrites
    {
        get; init;
    }
    internal required long VerifiedRecords
    {
        get; init;
    }
    internal required long VerificationPasses
    {
        get; init;
    }
    internal required long RuntimeReadCalls
    {
        get; init;
    }
    internal required long RetainedKeyBytes
    {
        get; init;
    }
    internal required long RetainedValueBytes
    {
        get; init;
    }
    internal required long RetainedOrderBytes
    {
        get; init;
    }
    internal required string FullValueDigest
    {
        get; init;
    }
    internal required string PermutationDigest
    {
        get; init;
    }
    internal required long ProcessPeakBytes
    {
        get; init;
    }
    internal required long? RuntimeResidentRecords
    {
        get; init;
    }
    internal required long SeedElapsedTicks
    {
        get; init;
    }
    internal required long VerificationElapsedTicks
    {
        get; init;
    }
    internal required long StopwatchFrequency
    {
        get; init;
    }
    internal required long EffectiveMemoryCapacityBytes
    {
        get; init;
    }
    internal required long ProcessMemoryCeilingBytes
    {
        get; init;
    }
    internal required long ProcessWorkingSetBytes
    {
        get; init;
    }
}
