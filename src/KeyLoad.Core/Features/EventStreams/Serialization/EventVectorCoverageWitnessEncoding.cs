using System.Security.Cryptography;

namespace KeyLoad.Core;

internal static class EventVectorCoverageWitnessEncoding
{
    private const int Version = 1;
    private const int FirstOrdinal = 0;
    private const long InitialCut = 0;
    private const string InvalidWitness = "The native event vector coverage witness encoding is inconsistent.";

    internal static EventVectorCoverageWitness Read(ReadOnlyMemory<byte> encoded,
        EventFeedControlRequest request, EventVectorAdmissionPolicy admission)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(admission);
        admission.RequireEncodedBytes(encoded.Length);
        if (encoded.IsEmpty)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidWitness); }
        var witness = NativeSerialization.Deserialize<EventVectorCoverageWitness>(encoded.Span);
        if (witness.Version != Version || witness.OriginalPackets is null || witness.FreshPackets is null
            || witness.OriginalSemanticDigest.Length != SHA256.HashSizeInBytes
            || witness.OriginalSemanticProjectionBytes.IsEmpty)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidWitness); }
        admission.RequireEntryCount(witness.OriginalPackets.Length);
        admission.RequireEntryCount(witness.FreshPackets.Length);
        var identity = NativeSerialization.Deserialize<EventVectorCoverageSemanticIdentity>(
            witness.OriginalSemanticProjectionBytes.Span);
        if (identity.Version != Version || identity.OriginalCaptureBytes is null
            || identity.OriginalCaptureBytes.Length != witness.OriginalPackets.Length
            || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(witness.OriginalSemanticProjectionBytes.Span),
                witness.OriginalSemanticDigest.Span))
        { throw Errors.Fail(ErrorCode.Corruption, InvalidWitness); }
        RequirePackets(request, witness.OriginalPackets, admission);
        RequirePackets(request, witness.FreshPackets, admission);
        _ = EventVectorEntryEncoding.Decode(identity.OriginalEncodedEntries,
            EventVectorEntryEncoding.Digest(identity.OriginalEncodedEntries), admission);
        for (var ordinal = FirstOrdinal; ordinal < witness.OriginalPackets.Length; ordinal++)
        {
            if (!identity.OriginalCaptureBytes[ordinal].Span.SequenceEqual(
                    witness.OriginalPackets[ordinal].OriginalCaptureBytes.Span))
            { throw Errors.Fail(ErrorCode.Corruption, InvalidWitness); }
        }
        return witness;
    }

    private static void RequirePackets(EventFeedControlRequest request, EventVectorCoveragePacket[] packets,
        EventVectorAdmissionPolicy admission)
    {
        for (var ordinal = FirstOrdinal; ordinal < packets.Length; ordinal++)
        {
            var packet = packets[ordinal];
            if (packet is null || packet.Version != Version || packet.RequestBytes.IsEmpty
                || packet.RequestSignature.IsEmpty || packet.ReplyBytes.IsEmpty
                || packet.ReplySignature.IsEmpty || packet.OriginalCaptureBytes.IsEmpty)
            { throw Errors.Fail(ErrorCode.Corruption, InvalidWitness); }
            admission.RequireEncodedBytes(packet.OriginalCaptureBytes.Length);
            var capture = NativeSerialization.Deserialize<EventVectorCoverageCapture>(packet.OriginalCaptureBytes.Span);
            if (capture.Version != Version || capture.MapId != request.MapId
                || capture.ControlPartition != request.ControlPartition || capture.Scope != request.Scope
                || capture.GroupOrdinal != ordinal || capture.SourceOwner is null || capture.NodeId == Guid.Empty
                || string.IsNullOrWhiteSpace(capture.PrincipalId) || capture.HasMore
                || capture.ReadGeneration < InitialCut || capture.StoreCutPosition < InitialCut
                || capture.AppliedCutPosition < InitialCut)
            { throw Errors.Fail(ErrorCode.Corruption, InvalidWitness); }
            _ = EventVectorEntryEncoding.Decode(capture.OriginalEncodedEntries, capture.EntryChecksum, admission);
        }
    }
}
