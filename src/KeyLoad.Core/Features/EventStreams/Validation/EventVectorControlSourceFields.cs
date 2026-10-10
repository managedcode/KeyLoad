namespace KeyLoad.Core;

internal static class EventVectorControlSourceFields
{
    private const long NextCoverage = 1;
    private const long InitialMapRevision = 1;
    private const long InitialCoverage = 1;
    private const string InvalidSource = "The native event vector control source binding is inconsistent.";

    internal static void Require(EventVectorControlPhase phase, EventVectorAdmissionPolicy admission)
    {
        ArgumentNullException.ThrowIfNull(phase);
        ArgumentNullException.ThrowIfNull(admission);
        var source = phase.IntendedSourcePhase ?? phase.ObservedSourcePhase;
        if (source is null)
        { return; }
        var request = phase.OriginalRequest;
        var initial = phase.Action == EventVectorControlAction.Open;
        var coverageMatches = initial && source.CoverageGeneration == InitialCoverage
            || !initial && source.CoverageGeneration == phase.ExpectedCoverageGeneration
            || request.Action == EventFeedControlAction.Refresh
                && source.CoverageGeneration == checked(phase.ExpectedCoverageGeneration + NextCoverage);
        if (!phase.OriginalCleanupFrontierBytes.IsEmpty)
        {
            EventVectorCleanupSourceBinding.Require(phase, source, admission);
            coverageMatches = true;
        }
        var roleMatches = request.Action switch
        {
            EventFeedControlAction.Open => source.Role == EventVectorSourcePhaseRole.Pin,
            EventFeedControlAction.Acknowledge => source.Role == EventVectorSourcePhaseRole.Advance,
            EventFeedControlAction.Refresh => source.Role is EventVectorSourcePhaseRole.Pin
                or EventVectorSourcePhaseRole.Advance or EventVectorSourcePhaseRole.Release,
            EventFeedControlAction.Release => source.Role == EventVectorSourcePhaseRole.Release,
            _ => false
        };
        if (source.MapId != request.MapId || source.ControlPartition != request.ControlPartition
            || source.MapRevision != (initial ? InitialMapRevision : phase.ExpectedMapRevision) || !coverageMatches || !roleMatches
            || source.CleanupGeneration != phase.ExpectedCleanupGeneration)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidSource); }
        if (phase.IntendedSourcePhase is not null)
        { _ = EventVectorSourceMutationValidation.ReadOriginal(source, admission); }
        else
        {
            if (phase.ExpectedPendingSourcePhaseId != source.PhaseCommandId)
            { throw Errors.Fail(ErrorCode.Corruption, InvalidSource); }
            _ = EventVectorSourceMutationValidation.ReadObserved(source, admission);
        }
    }
}
