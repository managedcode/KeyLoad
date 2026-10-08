using System.Collections.Immutable;

namespace KeyLoad.Core.Features.Search;

internal readonly record struct AnnSeedCut(Guid NodeId, Guid Incarnation,
    int StoreFormatVersion, int KeyCodecVersion, long ReadGeneration,
    long Position, long AppliedPosition, long OutboxTail, long OutboxFirstAvailable);

internal sealed record AnnSeedScope(string PrincipalId, long PolicyEpoch,
    PartitionRef Partition, string Collection, string Field, long SchemaVersion,
    VectorSpace Space, DateTimeOffset EvaluatedAt);

internal sealed record AnnSeed(AnnSeedScope Scope, AnnSeedCut Cut,
    ImmutableArray<VectorRecord> Records, string CorpusSha256,
    long OwnedBytesUpperBound, long PeakBytesUpperBound, long ReadBytes, long WorkUnits)
{
    internal string? DependencySha256 { get; init; }
    internal long? ProjectionCheckpoint { get; init; }
}

internal sealed record AnnSeedCaptured(AnnSeedScope Scope, AnnSeedCut Cut,
    ImmutableArray<VectorRecord> Records, byte[] HashScratch, long OwnedBytes, long PeakBytes, AnnSeedWork Work)
{
    internal string? DependencySha256 { get; init; }
    internal long? ProjectionCheckpoint { get; init; }
}
