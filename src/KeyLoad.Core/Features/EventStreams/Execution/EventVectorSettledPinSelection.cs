using System.Collections.Immutable;

namespace KeyLoad.Core;

internal static class EventVectorSettledPinSelection
{
    private const string Invalid = "The original settled event vector Pin inventory requires recovery.";

    internal static ImmutableArray<EventVectorSourcePhase> Read(EventVectorOwnedInventory inventory,
        ImmutableArray<EventVectorEntry> entries, EventVectorAdmissionPolicy admission)
    {
        var map = inventory.Header ?? throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        if (map.PendingPhaseId is not null || map.Released)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
        var latest = new Dictionary<EventSourceRef, EventVectorSourcePhase>();
        var revisions = new HashSet<(EventSourceRef Source, long Revision)>();
        var originals = new Dictionary<Guid, EventVectorSourcePhase>();
        var prefix = EventVectorKeys.Phases(map.ControlPartition, map.MapId);
        foreach (var row in inventory.PayloadRows)
        {
            if (!row.Key.Span.StartsWith(prefix))
            { continue; }
            var phase = EventVectorInventoryPhaseValidation.Read(map, row, admission);
            if (phase.Role == EventVectorSourcePhaseRole.Pin
                && phase.Disposition == EventVectorSourcePhaseDisposition.Observed
                && phase.SourceOriginalResult is { Error: null } originalResult)
            {
                var originalMutation = EventVectorSourceMutationValidation.ReadObserved(phase, admission);
                EventVectorObservedPinValidation.Require(phase, originalMutation,
                    originalResult.Get<EventVectorSourceResult>(), admission);
                if (!originals.TryAdd(phase.PhaseCommandId, phase))
                { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
            }
            if (phase.CoverageGeneration != map.CoverageGeneration)
            { continue; }
            if (phase.Disposition != EventVectorSourcePhaseDisposition.Observed
                || phase.SourceOriginalResult is null || phase.SourceOriginalResult.Error is not null)
            { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
            if (phase.Role is not (EventVectorSourcePhaseRole.Pin or EventVectorSourcePhaseRole.Advance))
            { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
            var mutation = EventVectorSourceMutationValidation.ReadObserved(phase, admission);
            var result = phase.SourceOriginalResult.Get<EventVectorSourceResult>();
            EventVectorObservedPinValidation.Require(phase, mutation, result, admission);
            if (!revisions.Add((phase.Source, result.Pin.PinRevision)))
            { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
            if (!latest.TryGetValue(phase.Source, out var previous)
                || previous.SourceOriginalResult!.Get<EventVectorSourceResult>().Pin.PinRevision < result.Pin.PinRevision)
            { latest[phase.Source] = phase; }
        }
        if (latest.Count != entries.Length)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
        return RequireOrdered(entries, latest, originals, admission);
    }

    private static ImmutableArray<EventVectorSourcePhase> RequireOrdered(ImmutableArray<EventVectorEntry> entries,
        Dictionary<EventSourceRef, EventVectorSourcePhase> latest,
        Dictionary<Guid, EventVectorSourcePhase> originals, EventVectorAdmissionPolicy admission)
    {
        var ordered = ImmutableArray.CreateBuilder<EventVectorSourcePhase>(entries.Length);
        foreach (var entry in entries)
        {
            if (!latest.TryGetValue(entry.Source, out var phase))
            { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
            var pin = phase.SourceOriginalResult!.Get<EventVectorSourceResult>().Pin;
            if (pin.Position != entry.Position || pin.PrincipalId != entry.PrincipalId
                || pin.SourcePolicyEpoch != entry.PolicyEpoch || pin.SourceSchemaVersion != entry.SchemaVersion
                || phase.SourceOwner.Incarnation != entry.Incarnation
                || phase.SourceOwner.PlacementEpoch != entry.PlacementEpoch
                || phase.SourceLogicalPlacementRevision != entry.LogicalPlacementRevision
                || phase.SourceDirectoryFence != entry.DirectoryFence
                || !originals.TryGetValue(pin.OriginalPinCommandId, out var original))
            { throw Errors.Fail(ErrorCode.TokenInvalidated, Invalid); }
            var mutation = EventVectorSourceMutationValidation.ReadObserved(phase, admission);
            if (phase.Role == EventVectorSourcePhaseRole.Advance
                && !mutation.OriginalPinNativeBody.Span.SequenceEqual(original.OriginalNativeBody.Span))
            { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
            ordered.Add(phase);
        }
        return ordered.ToImmutable();
    }
}
