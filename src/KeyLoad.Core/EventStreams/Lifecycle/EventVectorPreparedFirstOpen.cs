using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace KeyLoad.Core;

internal sealed class EventVectorPreparedFirstOpen(EventVectorControlPhase phase) : IDisposable
{
    internal EventVectorControlPhase Phase { get; } = phase;

    public void Dispose()
    {
        Clear(Phase.OriginalEncodedEntries);
        Clear(Phase.OriginalEntryChecksum);
        if (Phase.IntendedSourcePhase is { } pin)
        {
            Clear(pin.OriginalNativeBody);
            Clear(pin.OriginalBodyDigest);
        }
    }

    private static void Clear(ReadOnlyMemory<byte> bytes)
        => CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(bytes).Span);
}
