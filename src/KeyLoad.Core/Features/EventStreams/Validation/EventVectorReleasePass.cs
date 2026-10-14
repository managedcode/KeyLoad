using System.Collections.Immutable;

namespace KeyLoad.Core;

internal static class EventVectorReleasePass
{
    private const int Version = 1;
    private const long NextGeneration = 1;
    private const int FirstSourceOrdinal = 0;
    private const string InvalidPass = "The complete event vector release pass is inconsistent.";

    internal static EventVectorMap Complete(EventVectorMap map, ImmutableArray<EventVectorEntry> entries,
        ImmutableArray<EventVectorSourcePhase> observed, EventVectorEncodedRow originalProofRow,
        EventVectorAdmissionPolicy admission)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(admission);
        if (map.Version != Version || map.State != EventVectorMapState.Releasing || map.Released
            || map.PendingPhaseId is not null || entries.IsDefault || observed.IsDefault
            || entries.Length != observed.Length)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidPass);
        }
        EventVectorReleaseCoverage.Require(map, originalProofRow, entries, admission);
        admission.RequireEntryCount(entries.Length);
        var retained = false;
        var identities = new HashSet<Guid>();
        for (var ordinal = FirstSourceOrdinal; ordinal < entries.Length; ordinal++)
        {
            var phase = observed[ordinal];
            if (phase is null || !identities.Add(phase.PhaseCommandId)
                || phase.MapId != map.MapId || phase.ControlPartition != map.ControlPartition
                || phase.ControlIncarnation != map.ControlIncarnation || phase.PrincipalId != map.PrincipalId
                || phase.MapRevision != map.Revision || phase.CoverageGeneration != map.CoverageGeneration
                || phase.CleanupGeneration != map.CleanupGeneration || phase.Role != EventVectorSourcePhaseRole.Release
                || phase.SourceOrdinal != ordinal || phase.Source != entries[ordinal].Source)
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidPass);
            }
            var request = EventVectorSourceMutationValidation.ReadObserved(phase, admission);
            var result = phase.SourceOriginalResult!.Get<EventVectorSourceReleaseResult>();
            EventVectorReleaseResultValidation.Require(phase, request, result, admission);
            retained |= result.Disposition == EventVectorReleaseDisposition.Retained;
        }
        return map with
        {
            State = retained ? EventVectorMapState.Releasing : EventVectorMapState.Released,
            Released = !retained,
            CleanupGeneration = retained ? checked(map.CleanupGeneration + NextGeneration) : map.CleanupGeneration
        };
    }

}
