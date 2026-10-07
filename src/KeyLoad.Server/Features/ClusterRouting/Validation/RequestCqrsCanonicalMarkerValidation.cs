namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsCanonicalMarkerValidation
{
    private const long FirstCanonicalEntryIndex = 1;
    private const long FirstCanonicalEntryTerm = 1;
    internal static bool Valid(RequestCqrsProbeMarkerRecord marker)
    {
        var canonical = marker.Phase is RequestCqrsProbePhase.CanonicalJournalFlushed
            or RequestCqrsProbePhase.CanonicalOutboundObserved or RequestCqrsProbePhase.CanonicalIndependentAppendCompleted
            or RequestCqrsProbePhase.CanonicalOwnerDisposed;
        return canonical ? marker.EntryIndex is >= FirstCanonicalEntryIndex
            && marker.EntryTerm is >= FirstCanonicalEntryTerm && marker.CommandId != Guid.Empty
            : marker.EntryIndex is null && marker.EntryTerm is null;
    }
}
