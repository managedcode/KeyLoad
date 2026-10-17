using System.Collections.Immutable;

namespace KeyLoad.Core;

internal sealed class EventVectorPreparedRefresh(EventFeedControlRequest request, EventVectorMap map, PhysicalShardRecord controlOwner,
    ImmutableArray<EventVectorEntry> prior,
    ReadOnlyMemory<byte> witness, ImmutableArray<ReadOnlyMemory<byte>> pins,
    ImmutableArray<EventVectorEntry> candidates, ReadOnlyMemory<byte> encoded, ReadOnlyMemory<byte> checksum) : IDisposable
{
    internal EventFeedControlRequest Request { get; } = request;
    internal EventVectorMap Map { get; } = map;
    internal PhysicalShardRecord ControlOwner { get; } = controlOwner;
    internal EventVectorPreparedOffer Original { get; } = new(map, prior, witness, pins);
    internal ImmutableArray<EventVectorEntry> Candidates { get; } = candidates;
    internal ReadOnlyMemory<byte> EncodedCandidates { get; } = encoded;
    internal ReadOnlyMemory<byte> CandidateChecksum { get; } = checksum;

    public void Dispose()
    {
        EventVectorSettlementTargetEncoding.Clear(EncodedCandidates);
        EventVectorSettlementTargetEncoding.Clear(CandidateChecksum);
        Original.Dispose();
    }
}
