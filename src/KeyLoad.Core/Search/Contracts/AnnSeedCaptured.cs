using System.Collections.Immutable;

namespace KeyLoad.Core.Search;

internal sealed record AnnSeedCaptured(AnnSeedScope Scope, AnnSeedCut Cut,
    ImmutableArray<VectorRecord> Records, byte[] HashScratch, long OwnedBytes, long PeakBytes, AnnSeedWork Work)
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
