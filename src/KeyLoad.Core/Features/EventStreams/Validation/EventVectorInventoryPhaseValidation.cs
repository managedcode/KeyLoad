namespace KeyLoad.Core;

internal static class EventVectorInventoryPhaseValidation
{
    private const long NextCoverage = 1;
    private const string InvalidPhase = "The native event vector retained phase inventory is inconsistent.";

    internal static EventVectorSourcePhase Read(EventVectorMap header, EventVectorEncodedRow row,
        EventVectorAdmissionPolicy admission, long? maximumCoverageGeneration = null)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(admission);
        var phase = NativeSerialization.Deserialize<EventVectorSourcePhase>(row.Value.Span);
        if (phase.MapId != header.MapId || phase.ControlPartition != header.ControlPartition
            || phase.ControlIncarnation != header.ControlIncarnation || phase.PrincipalId != header.PrincipalId
            || phase.MapRevision > header.Revision || phase.CoverageGeneration > (maximumCoverageGeneration ??
                (header.State == EventVectorMapState.Refreshing
                    ? checked(header.CoverageGeneration + NextCoverage) : header.CoverageGeneration))
            || phase.CleanupGeneration > header.CleanupGeneration || !row.Key.Span.SequenceEqual(
                EventVectorKeys.Phase(header.ControlPartition, header.MapId, phase.PhaseCommandId)))
        { throw Errors.Fail(ErrorCode.Corruption, InvalidPhase); }
        if (phase.Disposition == EventVectorSourcePhaseDisposition.Admitted)
        { _ = EventVectorSourceMutationValidation.ReadOriginal(phase, admission); }
        else
        { _ = EventVectorSourceMutationValidation.ReadObserved(phase, admission); }
        return phase;
    }

    internal static void RequirePointers(EventVectorMap header,
        IReadOnlyDictionary<Guid, EventVectorSourcePhase> phases)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(phases);
        if (header.PendingPhaseId is { } pending && (!phases.TryGetValue(pending, out var admitted)
                || admitted.Disposition != EventVectorSourcePhaseDisposition.Admitted)
            || header.LastSettledPhaseId is { } settled && (!phases.TryGetValue(settled, out var observed)
                || observed.Disposition != EventVectorSourcePhaseDisposition.Observed))
        { throw Errors.Fail(ErrorCode.Corruption, InvalidPhase); }
    }
}
