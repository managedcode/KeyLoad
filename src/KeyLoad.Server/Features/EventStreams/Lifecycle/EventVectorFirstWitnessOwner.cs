using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Core;

namespace KeyLoad.Server.Features.EventStreams;

internal sealed class EventVectorFirstWitnessOwner : IDisposable
{
    private const int Version = 1;
    private const int FirstOrdinal = 0;
    private const int RetainedArrayCount = 3;
    private byte[] entries = [];
    private byte[] projection = [];
    private byte[] digest = [];
    private byte[] witness = [];
    private byte[][] captures = [];

    internal ReadOnlyMemory<byte> OriginalWitnessBytes => witness;

    internal static EventVectorFirstWitnessOwner Create(ImmutableArray<EventVectorEntry> originalEntries,
        EventVectorCapturedExchange[] originalCaptures, EventVectorAdmissionPolicy admission,
        EventVectorRemoteEncoding encoding, ReadExecutionBudget work)
    {
        var owner = new EventVectorFirstWitnessOwner();
        var transferred = false;
        try
        {
            owner.Encode(originalEntries, originalCaptures, admission, encoding, work);
            transferred = true;
            return owner;
        }
        finally { if (!transferred) { owner.Dispose(); } }
    }

    private void Encode(ImmutableArray<EventVectorEntry> originalEntries,
        EventVectorCapturedExchange[] originalCaptures, EventVectorAdmissionPolicy admission,
        EventVectorRemoteEncoding encoding, ReadExecutionBudget work)
    {
        admission.RequireEncodedBytes(NativeSerialization.Measure(originalEntries));
        entries = EventVectorWorkEncoding.Encode(originalEntries, encoding, work);
        work.ChargeBytes(checked((long)originalCaptures.Length * IntPtr.Size * RetainedArrayCount));
        captures = new byte[originalCaptures.Length][];
        var packets = new EventVectorCoveragePacket[originalCaptures.Length];
        var captureBytes = new ReadOnlyMemory<byte>[originalCaptures.Length];
        for (var ordinal = FirstOrdinal; ordinal < originalCaptures.Length; ordinal++)
        {
            var packet = originalCaptures[ordinal].OriginalPacket;
            admission.RequireEncodedBytes(packet.OriginalCaptureBytes.Length);
            work.ChargeBytes(packet.OriginalCaptureBytes.Length);
            captures[ordinal] = packet.OriginalCaptureBytes.ToArray();
            captureBytes[ordinal] = captures[ordinal];
            packets[ordinal] = packet;
        }
        var semantic = new EventVectorCoverageSemanticIdentity
        {
            Version = Version,
            OriginalEncodedEntries = entries,
            OriginalCaptureBytes = captureBytes
        };
        admission.RequireEncodedBytes(NativeSerialization.Measure(semantic));
        projection = EventVectorWorkEncoding.Encode(semantic, encoding, work);
        work.ChargeBytes(SHA256.HashSizeInBytes);
        digest = SHA256.HashData(projection);
        var value = new EventVectorCoverageWitness
        {
            Version = Version,
            OriginalSemanticProjectionBytes = projection,
            OriginalSemanticDigest = digest,
            OriginalPackets = packets,
            FreshPackets = packets
        };
        admission.RequireEncodedBytes(NativeSerialization.Measure(value));
        witness = EventVectorWorkEncoding.Encode(value, encoding, work);
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(entries);
        CryptographicOperations.ZeroMemory(projection);
        CryptographicOperations.ZeroMemory(digest);
        CryptographicOperations.ZeroMemory(witness);
        foreach (var capture in captures)
        {
            if (capture is not null)
            {
                CryptographicOperations.ZeroMemory(capture);
            }
        }
        entries = [];
        projection = [];
        digest = [];
        witness = [];
        captures = [];
    }
}
