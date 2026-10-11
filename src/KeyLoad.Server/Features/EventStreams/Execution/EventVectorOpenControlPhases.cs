using KeyLoad.Core;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorOpenControlPhases
{
    internal static EventVectorControlPhase Admit(EventVectorControlPhase original,
        EventVectorSourcePhase intended)
        => Source(original, intended) with
        { Action = EventVectorControlAction.AdmitSource, IntendedSourcePhase = intended };

    internal static EventVectorControlPhase Observe(EventVectorControlPhase original,
        EventVectorSourcePhase observed)
        => Source(original, observed) with
        {
            Action = EventVectorControlAction.ObserveSource,
            ObservedSourcePhase = observed,
            ExpectedPendingSourcePhaseId = observed.PhaseCommandId
        };

    internal static EventVectorControlPhase Publish(EventVectorControlPhase original, EventVectorMap opening)
        => original with
        {
            Action = EventVectorControlAction.PublishOpen,
            ExpectedMapRevision = opening.Revision,
            ExpectedCoverageGeneration = opening.CoverageGeneration,
            ExpectedCleanupGeneration = opening.CleanupGeneration,
            IntendedSourcePhase = null,
            ObservedSourcePhase = null,
            ExpectedPendingSourcePhaseId = null
        };

    private static EventVectorControlPhase Source(EventVectorControlPhase original, EventVectorSourcePhase phase)
        => original with
        {
            ExpectedMapRevision = phase.MapRevision,
            ExpectedCoverageGeneration = phase.CoverageGeneration,
            ExpectedCleanupGeneration = phase.CleanupGeneration,
            OriginalEncodedEntries = ReadOnlyMemory<byte>.Empty,
            OriginalEntryChecksum = ReadOnlyMemory<byte>.Empty,
            IntendedSourcePhase = null,
            ObservedSourcePhase = null,
            ExpectedPendingSourcePhaseId = null
        };
}
