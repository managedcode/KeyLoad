using System.Collections.Immutable;

namespace KeyLoad.Core;

internal static class EventVectorInventoryCoverageBound
{
    private const long CoverageStep = 1;
    private const string Invalid = "The retained original refresh coverage requires recovery.";

    internal static long Read(EventVectorMap map, ImmutableArray<EventVectorEncodedRow> rows,
        EventVectorAdmissionPolicy admission)
    {
        if (map.State == EventVectorMapState.Refreshing)
        { return checked(map.CoverageGeneration + CoverageStep); }
        if (map.State != EventVectorMapState.Releasing)
        { return map.CoverageGeneration; }
        var cleanupKey = EventVectorKeys.CleanupCall(map.ControlPartition, map.MapId);
        var parentKey = EventVectorKeys.ParentCall(map.ControlPartition, map.MapId);
        var cleanupRow = rows.SingleOrDefault(row => row.Key.Span.SequenceEqual(cleanupKey));
        if (cleanupRow.Key.IsEmpty)
        { return map.CoverageGeneration; }
        var cleanup = EventVectorCleanupValidation.Read(map, cleanupRow, admission);
        var parentRow = rows.SingleOrDefault(row => row.Key.Span.SequenceEqual(parentKey));
        if (parentRow.Key.IsEmpty)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
        var parent = EventVectorParentValidation.Read(map, parentRow, admission);
        if (parent.OriginalPublicCommandId != cleanup.OriginalParentCommandId
            || !parent.OriginalPublicRequestDigest.Span.SequenceEqual(cleanup.OriginalParentRequestDigest.Span)
            || !parent.OriginalAdmittedControlPhaseBytes.Span.SequenceEqual(cleanup.OriginalAdmittedPhaseBytes.Span))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
        var original = EventVectorParentValidation.ReadPhase(parent, admission);
        return original.OriginalRequest.Action == EventFeedControlAction.Refresh
            && original.ExpectedCoverageGeneration == map.CoverageGeneration
            ? checked(map.CoverageGeneration + CoverageStep) : map.CoverageGeneration;
    }
}
