namespace KeyLoad.Core;

internal static class EventVectorControlFirstPin
{
    private const int FirstOrdinal = 0;
    private const string InvalidFirstPin = "The original first event vector Pin admission is inconsistent.";

    internal static void Require(EventVectorMap map, EventVectorControlPhase control,
        EventVectorSourcePhase first, EventVectorAdmissionPolicy admission)
    {
        var entries = EventVectorEntryEncoding.Decode(control.OriginalEncodedEntries,
            control.OriginalEntryChecksum, admission);
        var original = EventVectorSourceMutationValidation.ReadOriginal(first, admission);
        if (entries.IsEmpty || first.SourceOrdinal != FirstOrdinal || first.Role != EventVectorSourcePhaseRole.Pin
            || first.Source != entries[FirstOrdinal].Source || first.MapRevision != map.Revision
            || first.CoverageGeneration != map.CoverageGeneration || first.MapId != map.MapId
            || first.ControlPartition != map.ControlPartition || first.ControlIncarnation != map.ControlIncarnation
            || first.PrincipalId != map.PrincipalId || first.ControlPolicyEpoch != map.ControlPolicyEpoch
            || first.OriginalPosition != entries[FirstOrdinal].Position || first.TargetPosition != entries[FirstOrdinal].Position
            || first.SourceOwner.Incarnation != entries[FirstOrdinal].Incarnation
            || first.SourceOwner.PlacementEpoch != entries[FirstOrdinal].PlacementEpoch
            || original.SourcePolicyEpoch != entries[FirstOrdinal].PolicyEpoch
            || original.SourceSchemaVersion != entries[FirstOrdinal].SchemaVersion
            || original.LogicalPlacementRevision != entries[FirstOrdinal].LogicalPlacementRevision
            || original.DirectoryFence != entries[FirstOrdinal].DirectoryFence)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidFirstPin); }
    }
}

