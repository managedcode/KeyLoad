using System.Collections.Immutable;

namespace KeyLoad.Core;

internal static class EventVectorRefreshSettlement
{
    private const int FirstOrdinal = 0;
    private const long CoverageStep = 1;
    private const string UnsettledRefresh = "The original event vector refresh has unsettled source ownership.";

    internal static void Require(EventVectorOwnedInventory inventory, ImmutableArray<EventVectorEntry> candidate,
        EventVectorAdmissionPolicy admission)
    {
        var map = inventory.Header ?? throw Errors.Fail(ErrorCode.RecoveryRequired, UnsettledRefresh);
        var next = checked(map.CoverageGeneration + CoverageStep);
        var prior = EventVectorControlPages.Entries(inventory, admission);
        var pins = EventVectorControlPublication.Phases(inventory, EventVectorSourcePhaseRole.Pin, admission, next);
        var advances = EventVectorControlPublication.Phases(inventory, EventVectorSourcePhaseRole.Advance, admission, next);
        if (checked(pins.Count + advances.Count) != candidate.Length)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, UnsettledRefresh); }
        for (var ordinal = FirstOrdinal; ordinal < candidate.Length; ordinal++)
        {
            var existing = prior.Any(entry => entry.Source == candidate[ordinal].Source);
            var phases = existing ? advances : pins;
            if (!phases.TryGetValue(ordinal, out var phase) || phase.Source != candidate[ordinal].Source
                || phase.TargetPosition != candidate[ordinal].Position)
            { throw Errors.Fail(ErrorCode.RecoveryRequired, UnsettledRefresh); }
            RequireObservedPin(phase, admission);
        }
        RequireRemoved(inventory, prior, candidate, admission);
    }

    private static void RequireObservedPin(EventVectorSourcePhase phase, EventVectorAdmissionPolicy admission)
    {
        if (phase.Disposition != EventVectorSourcePhaseDisposition.Observed
            || phase.SourceOriginalResult is null || phase.SourceOriginalResult.Error is not null)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, UnsettledRefresh); }
        var request = EventVectorSourceMutationValidation.ReadObserved(phase, admission);
        EventVectorObservedPinValidation.Require(phase, request,
            phase.SourceOriginalResult.Get<EventVectorSourceResult>(), admission);
    }

    private static void RequireRemoved(EventVectorOwnedInventory inventory, ImmutableArray<EventVectorEntry> prior,
        ImmutableArray<EventVectorEntry> candidate, EventVectorAdmissionPolicy admission)
    {
        var releases = EventVectorControlPublication.Phases(inventory, EventVectorSourcePhaseRole.Release, admission);
        var removed = prior.Count(entry => !candidate.Any(current => current.Source == entry.Source));
        if (releases.Count != removed)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, UnsettledRefresh); }
        for (var ordinal = FirstOrdinal; ordinal < prior.Length; ordinal++)
        {
            if (candidate.Any(entry => entry.Source == prior[ordinal].Source))
            { continue; }
            if (!releases.TryGetValue(ordinal, out var phase) || phase.Source != prior[ordinal].Source
                || phase.Disposition != EventVectorSourcePhaseDisposition.Observed
                || phase.SourceOriginalResult is null || phase.SourceOriginalResult.Error is not null)
            { throw Errors.Fail(ErrorCode.RecoveryRequired, UnsettledRefresh); }
            var request = EventVectorSourceMutationValidation.ReadObserved(phase, admission);
            var result = phase.SourceOriginalResult.Get<EventVectorSourceReleaseResult>();
            EventVectorReleaseResultValidation.Require(phase, request, result, admission);
            if (result.Disposition != EventVectorReleaseDisposition.Released)
            { throw Errors.Fail(ErrorCode.RecoveryRequired, UnsettledRefresh); }
        }
    }
}
