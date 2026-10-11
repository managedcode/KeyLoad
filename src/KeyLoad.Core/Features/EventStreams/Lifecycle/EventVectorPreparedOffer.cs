using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace KeyLoad.Core;

internal sealed class EventVectorPreparedOffer : IDisposable
{
    internal long Revision { get; }
    internal long CoverageGeneration { get; }
    internal long CleanupGeneration { get; }
    internal ImmutableArray<EventVectorEntry> Entries { get; }
    internal ReadOnlyMemory<byte> OriginalCoverageWitness { get; }
    internal ImmutableArray<ReadOnlyMemory<byte>> OriginalPinPhaseBytes { get; }

    internal EventVectorPreparedOffer(EventVectorMap map, ImmutableArray<EventVectorEntry> entries,
        ReadOnlyMemory<byte> witness, ImmutableArray<ReadOnlyMemory<byte>> pins)
    {
        Revision = map.Revision;
        CoverageGeneration = map.CoverageGeneration;
        CleanupGeneration = map.CleanupGeneration;
        Entries = entries;
        OriginalCoverageWitness = witness;
        OriginalPinPhaseBytes = pins;
    }

    public void Dispose()
    {
        Clear(OriginalCoverageWitness);
        foreach (var pin in OriginalPinPhaseBytes)
        { Clear(pin); }
    }

    private static void Clear(ReadOnlyMemory<byte> bytes)
        => CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(bytes).Span);
}
