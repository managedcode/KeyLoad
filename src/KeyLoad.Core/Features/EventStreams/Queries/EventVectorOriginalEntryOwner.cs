namespace KeyLoad.Core;

internal static class EventVectorOriginalEntryOwner
{
    private const string Invalid = "The original event vector source owner is inconsistent.";

    internal static PhysicalShardRecord Require(EventVectorCoverageWitness witness,
        EventVectorEntry entry, EventVectorAdmissionPolicy admission, ReadExecutionBudget work)
    {
        PhysicalShardRecord? found = null;
        foreach (var packet in witness.OriginalPackets)
        {
            work.Check();
            admission.RequireEncodedBytes(packet.OriginalCaptureBytes.Length);
            work.ChargeBytes(packet.OriginalCaptureBytes.Length);
            var capture = NativeSerialization.Deserialize<EventVectorCoverageCapture>(packet.OriginalCaptureBytes.Span);
            try
            {
                var entries = EventVectorEntryEncoding.Decode(capture.OriginalEncodedEntries,
                    capture.EntryChecksum, admission);
                if (!entries.Any(original => original.Source == entry.Source))
                { continue; }
                if (!entries.Any(original => original == entry) || found is not null || capture.SourceOwner is null
                    || capture.SourceOwner.Incarnation != entry.Incarnation
                    || capture.SourceOwner.PlacementEpoch != entry.PlacementEpoch)
                { throw Errors.Fail(ErrorCode.OwnershipLost, Invalid); }
                found = capture.SourceOwner;
            }
            finally { EventVectorCoverageCaptureBuffers.Clear(capture); }
        }
        return found ?? throw Errors.Fail(ErrorCode.OwnershipLost, Invalid);
    }
}
