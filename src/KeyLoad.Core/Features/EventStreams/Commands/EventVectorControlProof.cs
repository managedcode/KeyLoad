namespace KeyLoad.Core;

internal static class EventVectorControlProof
{
    private const int Version = 1;

    internal static EventVectorCoverageProof Create(EventVectorControlPhase phase, long coverage,
        EventVectorAdmissionPolicy admission)
    {
        var witness = EventVectorCoverageWitnessEncoding.Read(phase.OriginalCoverageWitness,
            phase.OriginalRequest, admission);
        var original = NativeSerialization.Deserialize<EventVectorCoverageSemanticIdentity>(
            witness.OriginalSemanticProjectionBytes.Span);
        return new EventVectorCoverageProof
        {
            Version = Version,
            MapId = phase.OriginalRequest.MapId,
            ControlPartition = phase.OriginalRequest.ControlPartition,
            CoverageGeneration = coverage,
            OriginalSemanticDigest = witness.OriginalSemanticDigest,
            OriginalPackets = witness.OriginalPackets,
            OriginalEncodedEntries = original.OriginalEncodedEntries,
            EntryChecksum = EventVectorEntryEncoding.Digest(original.OriginalEncodedEntries),
            OriginalSemanticProjectionBytes = witness.OriginalSemanticProjectionBytes
        };
    }
}
