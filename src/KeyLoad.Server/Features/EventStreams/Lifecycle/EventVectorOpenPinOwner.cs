using System.Runtime.InteropServices;
using System.Security.Cryptography;
using KeyLoad.Core;

namespace KeyLoad.Server.Features.EventStreams;

internal sealed class EventVectorOpenPinOwner(EventVectorSourcePhase phase) : IDisposable
{
    internal EventVectorSourcePhase Phase => phase;

    public void Dispose()
    {
        Clear(phase.OriginalNativeBody);
        Clear(phase.OriginalBodyDigest);
        Clear(phase.SourceObservationWitness);
        Clear(phase.SourceOriginalOutcomeDigest);
    }

    private static void Clear(ReadOnlyMemory<byte> bytes)
        => CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(bytes).Span);
}
