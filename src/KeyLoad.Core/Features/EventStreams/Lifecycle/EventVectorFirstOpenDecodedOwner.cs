using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace KeyLoad.Core;

internal sealed class EventVectorFirstOpenDecodedOwner : IDisposable
{
    private readonly EventVectorCoverageWitness witness;
    internal EventVectorCoverageSemanticIdentity Projection
    {
        get;
    }

    internal EventVectorFirstOpenDecodedOwner(EventVectorCoverageWitness witness)
    {
        this.witness = witness;
        try
        {
            Projection = NativeSerialization.Deserialize<EventVectorCoverageSemanticIdentity>(
                witness.OriginalSemanticProjectionBytes.Span);
        }
        catch (Exception) { ClearWitness(); throw; }
    }

    public void Dispose()
    {
        Clear(Projection.OriginalEncodedEntries);
        foreach (var capture in Projection.OriginalCaptureBytes)
        {
            Clear(capture);
        }
        ClearWitness();
    }

    private void ClearWitness()
    {
        Clear(witness.OriginalSemanticDigest);
        Clear(witness.OriginalSemanticProjectionBytes);
        foreach (var packet in witness.OriginalPackets)
        {
            ClearPacket(packet);
        }
        foreach (var packet in witness.FreshPackets)
        {
            ClearPacket(packet);
        }
    }

    private static void ClearPacket(EventVectorCoveragePacket packet)
    {
        Clear(packet.RequestBytes);
        Clear(packet.RequestSignature);
        Clear(packet.ReplyBytes);
        Clear(packet.ReplySignature);
        Clear(packet.OriginalCaptureBytes);
    }

    private static void Clear(ReadOnlyMemory<byte> value)
        => CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(value).Span);
}
