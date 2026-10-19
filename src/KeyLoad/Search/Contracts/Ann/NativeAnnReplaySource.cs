using System.Collections.Immutable;
using KeyLoad.Core.Features.Search;

namespace KeyLoad.Server.Features.Search;

// Replay progress is deliberately separate from the actually observed canonical upper source cut.
internal sealed record NativeAnnReplaySource(AnnSeedScope Scope, string DependencySha256,
    ImmutableArray<VectorRecord> Records, long OwnedBytesUpperBound, long ThroughSequence)
{
    internal static NativeAnnReplaySource From(AnnSeed actual)
        => new(actual.Scope, actual.DependencySha256
            ?? throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.InvalidSource),
            actual.Records, actual.OwnedBytesUpperBound, actual.Cut.OutboxTail);
}
