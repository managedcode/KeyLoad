namespace KeyLoad.Core;

internal static class EventVectorControlPublication
{
    private const string InvalidPublication = "The original event vector publication inventory is inconsistent.";

    internal static EventVectorCoverageProof Proof(EventVectorOwnedInventory inventory,
        EventVectorAdmissionPolicy admission)
    {
        var map = inventory.Header ?? throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidPublication);
        var key = EventVectorKeys.Proof(map.ControlPartition, map.MapId, map.CoverageGeneration);
        foreach (var row in inventory.PayloadRows)
        {
            if (row.Key.Span.SequenceEqual(key))
            {
                return EventVectorCoverageProofValidation.Read(row, map, admission);
            }
        }
        throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidPublication);
    }

    internal static Dictionary<int, EventVectorSourcePhase> Phases(EventVectorOwnedInventory inventory,
        EventVectorSourcePhaseRole role, EventVectorAdmissionPolicy admission, long? coverageGeneration = null)
    {
        var map = inventory.Header ?? throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidPublication);
        var result = new Dictionary<int, EventVectorSourcePhase>();
        var prefix = EventVectorKeys.Phases(map.ControlPartition, map.MapId);
        foreach (var row in inventory.PayloadRows)
        {
            if (!row.Key.Span.StartsWith(prefix))
            {
                continue;
            }
            var phase = EventVectorInventoryPhaseValidation.Read(map, row, admission,
                EventVectorInventoryCoverageBound.Read(map, inventory.PayloadRows, admission));
            if (phase.Role != role || phase.MapRevision != map.Revision
                || phase.CoverageGeneration != (coverageGeneration ?? map.CoverageGeneration)
                || phase.CleanupGeneration != map.CleanupGeneration)
            {
                continue;
            }
            if (!result.TryAdd(phase.SourceOrdinal, phase))
            {
                throw Errors.Fail(ErrorCode.Corruption, InvalidPublication);
            }
        }
        return result;
    }
}
