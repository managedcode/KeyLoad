namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsCanonicalScopeValidation
{
    private const long FirstCanonicalEntryIndex = 1;
    private const long FirstCanonicalEntryTerm = 1;
    internal static bool Matches(RequestCqrsProbeArmRecord arm, RequestCqrsProbeMarkerRecord marker)
        => arm.Phase is RequestCqrsProbePhase.CanonicalJournalFlushed or RequestCqrsProbePhase.ReplicaEntryAcknowledged
            ? marker.RequestId == arm.SourceRequestId && marker.Voter == arm.TargetVoter
                && marker.EntryIndex is >= FirstCanonicalEntryIndex && marker.EntryTerm is >= FirstCanonicalEntryTerm
            : marker.EntryIndex is null && marker.EntryTerm is null;
}
