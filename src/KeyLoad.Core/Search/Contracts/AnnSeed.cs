using System.Collections.Immutable;

namespace KeyLoad.Core.Search;

internal sealed record AnnSeed(AnnSeedScope Scope, AnnSeedCut Cut,
    ImmutableArray<VectorRecord> Records, string CorpusSha256,
    long OwnedBytesUpperBound, long PeakBytesUpperBound, long ReadBytes, long WorkUnits)
{
    internal string? DependencySha256
    {
        get; init;
    }
    internal long? ProjectionCheckpoint
    {
        get; init;
    }
}
