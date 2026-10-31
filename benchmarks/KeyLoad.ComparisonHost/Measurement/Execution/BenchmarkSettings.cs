namespace KeyLoad.ComparisonHost.Measurement;

/// <summary>The standard dataset size and client schedule every database of a type runs.</summary>
/// <param name="Records">Records in the type's standard dataset.</param>
/// <param name="Operations">Measured operations per case.</param>
/// <param name="Clients">Concurrent clients per case.</param>
/// <param name="Seed">Seed of every deterministic choice.</param>
internal sealed record BenchmarkSettings(int Records, int Operations, int Clients, int Seed)
{
    internal const int StandardRecords = 1_000_000;
    internal const int StandardOperations = 100_000;
    internal const int StandardClients = 16;
    internal const int StandardSeed = 1729;

    /// <summary>Spreads <paramref name="index"/> deterministically over <c>[first, first + count)</c>.</summary>
    internal int Spread(int index, int first, int count)
    {
        const long Stride = 7919;
        return first + (int)(((index * Stride) + Seed) % count);
    }
}
