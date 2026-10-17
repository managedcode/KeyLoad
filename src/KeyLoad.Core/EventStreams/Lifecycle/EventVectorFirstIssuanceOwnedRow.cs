using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace KeyLoad.Core;

internal sealed class EventVectorFirstIssuanceOwnedRow(EventVectorFirstIssuance row) : IDisposable
{
    internal EventVectorFirstIssuance Row { get; } = row;

    public void Dispose()
    {
        Clear(Row.OriginalPublicRequestBytes);
        Clear(Row.OriginalPublicRequestDigest);
        Clear(Row.OriginalCoverageWitnessBytes);
        Clear(Row.OriginalCapturedFirstOpenPhaseBytes);
        Clear(Row.LastCleanupNativePayloadBytes);
    }

    private static void Clear(ReadOnlyMemory<byte> bytes)
        => CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(bytes).Span);
}
