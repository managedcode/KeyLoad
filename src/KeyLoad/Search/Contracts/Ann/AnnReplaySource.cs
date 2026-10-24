using System.Collections.Immutable;
using KeyLoad.Core.Search;

namespace KeyLoad.Server.Search;

// Replay progress is deliberately separate from the actually observed canonical upper source cut.
internal sealed record AnnReplaySource(AnnSeedScope Scope, string DependencySha256,
    ImmutableArray<VectorRecord> Records, long OwnedBytesUpperBound, long ThroughSequence)
{
    internal static AnnReplaySource From(AnnSeed actual)
        => new(actual.Scope, actual.DependencySha256
            ?? throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.InvalidSource),
            actual.Records, actual.OwnedBytesUpperBound, actual.Cut.OutboxTail);
}
