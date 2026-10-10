using System.Security.Cryptography;

namespace KeyLoad.Core;

internal static class EventVectorCoverageProofValidation
{
    private const int Version = 1;
    private const long FirstCoverage = 1;
    private const int FirstOrdinal = 0;
    private const string InvalidProof = "The retained native event vector coverage proof is inconsistent.";

    internal static EventVectorCoverageProof Read(EventVectorEncodedRow row, EventVectorMap map,
        EventVectorAdmissionPolicy admission)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(admission);
        admission.RequireEncodedBytes(row.EncodedBytes);
        var proof = NativeSerialization.Deserialize<EventVectorCoverageProof>(row.Value.Span);
        if (proof.Version != Version || proof.MapId != map.MapId || proof.ControlPartition != map.ControlPartition
            || proof.CoverageGeneration < FirstCoverage || proof.OriginalPackets is null
            || proof.OriginalSemanticDigest.Length != SHA256.HashSizeInBytes
            || proof.OriginalSemanticProjectionBytes.IsEmpty || !row.Key.Span.SequenceEqual(
                EventVectorKeys.Proof(map.ControlPartition, map.MapId, proof.CoverageGeneration)))
        { throw Errors.Fail(ErrorCode.Corruption, InvalidProof); }
        admission.RequireEncodedBytes(proof.OriginalSemanticProjectionBytes.Length);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(proof.OriginalSemanticProjectionBytes.Span),
                proof.OriginalSemanticDigest.Span))
        { throw Errors.Fail(ErrorCode.Corruption, InvalidProof); }
        _ = EventVectorEntryEncoding.Decode(proof.OriginalEncodedEntries, proof.EntryChecksum, admission);
        RequireOriginalPackets(proof, admission);
        return proof;
    }

    private static void RequireOriginalPackets(EventVectorCoverageProof proof, EventVectorAdmissionPolicy admission)
    {
        var identity = NativeSerialization.Deserialize<EventVectorCoverageSemanticIdentity>(
            proof.OriginalSemanticProjectionBytes.Span);
        if (identity.Version != Version || identity.OriginalCaptureBytes is null
            || !identity.OriginalEncodedEntries.Span.SequenceEqual(proof.OriginalEncodedEntries.Span)
            || identity.OriginalCaptureBytes.Length != proof.OriginalPackets.Length)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidProof); }
        admission.RequireEntryCount(proof.OriginalPackets.Length);
        foreach (var packet in proof.OriginalPackets)
        {
            if (packet is null || packet.Version != Version || packet.RequestBytes.IsEmpty
                || packet.RequestSignature.IsEmpty || packet.ReplyBytes.IsEmpty
                || packet.ReplySignature.IsEmpty || packet.OriginalCaptureBytes.IsEmpty)
            { throw Errors.Fail(ErrorCode.Corruption, InvalidProof); }
        }
        for (var ordinal = FirstOrdinal; ordinal < proof.OriginalPackets.Length; ordinal++)
        {
            if (!identity.OriginalCaptureBytes[ordinal].Span.SequenceEqual(
                    proof.OriginalPackets[ordinal].OriginalCaptureBytes.Span))
            { throw Errors.Fail(ErrorCode.Corruption, InvalidProof); }
        }
    }
}
