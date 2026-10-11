using System.Collections.Immutable;

namespace KeyLoad.Core;

internal static class EventVectorOpenPinPrefix
{
    private const int FirstOrdinal = 0;
    private const string Invalid = "The original event vector opening Pin prefix is inconsistent.";

    internal static void Require(EventVectorMap map, EventVectorOwnedInventory inventory,
        EventVectorControlPhase original, ImmutableArray<EventVectorEntry> entries, int ordinal,
        EventVectorAdmissionPolicy admission)
    {
        if (original.Action != EventVectorControlAction.Open || ordinal <= FirstOrdinal || ordinal >= entries.Length
            || map.State != EventVectorMapState.Opening || map.PendingPhaseId is not null
            || map.OriginalOpenCommandId != original.OriginalRequest.CommandId
            || !EventVectorControlPublication.Proof(inventory, admission).OriginalEncodedEntries.Span
                .SequenceEqual(original.OriginalEncodedEntries.Span))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
        var phases = EventVectorControlPublication.Phases(inventory, EventVectorSourcePhaseRole.Pin, admission);
        if (phases.Count != ordinal)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
        for (var index = FirstOrdinal; index < ordinal; index++)
        {
            if (!phases.TryGetValue(index, out var phase) || phase.Source != entries[index].Source
                || phase.TargetPosition != entries[index].Position
                || phase.Disposition != EventVectorSourcePhaseDisposition.Observed
                || phase.SourceOriginalResult is null || phase.SourceOriginalResult.Error is not null)
            { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
            var mutation = EventVectorSourceMutationValidation.ReadObserved(phase, admission);
            EventVectorObservedPinValidation.Require(phase, mutation,
                phase.SourceOriginalResult.Get<EventVectorSourceResult>(), admission);
            if (mutation.CoverageGeneration != map.CoverageGeneration)
            { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
        }
    }
}
