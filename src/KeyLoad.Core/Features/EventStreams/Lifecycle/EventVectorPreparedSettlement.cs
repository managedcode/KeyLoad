using System.Collections.Immutable;

namespace KeyLoad.Core;

internal sealed class EventVectorPreparedSettlement(EventVectorMap map, ImmutableArray<EventVectorEntry> entries, ReadOnlyMemory<byte> witness,
    ImmutableArray<ReadOnlyMemory<byte>> pins,
    ImmutableArray<EventVectorEntry> targetEntries, ReadOnlyMemory<byte> targetBytes,
    ReadOnlyMemory<byte> targetDigest, ImmutableArray<long> currentSourcePolicyEpochs) : IDisposable
{
    internal EventVectorMap Map { get; } = map;
    internal EventVectorPreparedOffer Owned { get; } = new(map, entries, witness, pins);
    internal ImmutableArray<EventVectorEntry> TargetEntries { get; } = targetEntries;
    internal ReadOnlyMemory<byte> TargetBytes { get; } = targetBytes;
    internal ReadOnlyMemory<byte> TargetDigest { get; } = targetDigest;

    internal ImmutableArray<long> CurrentSourcePolicyEpochs { get; } = currentSourcePolicyEpochs;

    public void Dispose()
    {
        EventVectorSettlementTargetEncoding.Clear(TargetBytes);
        EventVectorSettlementTargetEncoding.Clear(TargetDigest);
        Owned.Dispose();
    }
}
