using System.Collections.Immutable;

namespace KeyLoad.Core;

internal static class EventVectorOfferPinSelection
{
    private const string Invalid = "The original event vector offer pin inventory is inconsistent.";

    internal static ImmutableArray<EventVectorSourcePhase> Read(EventVectorOwnedInventory inventory,
        ImmutableArray<EventVectorEntry> entries, EventVectorAdmissionPolicy admission)
    {
        var map = inventory.Header ?? throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        var prefix = EventVectorKeys.Phases(map.ControlPartition, map.MapId);
        var pins = new Dictionary<EventSourceRef, EventVectorSourcePhase>();
        foreach (var row in inventory.PayloadRows)
        {
            if (!row.Key.Span.StartsWith(prefix))
            {
                continue;
            }
            var phase = EventVectorInventoryPhaseValidation.Read(map, row, admission);
            if (phase.Role != EventVectorSourcePhaseRole.Pin
                || phase.CoverageGeneration != map.CoverageGeneration)
            {
                continue;
            }
            if (phase.Disposition != EventVectorSourcePhaseDisposition.Observed
                || phase.SourceOriginalResult is null || phase.SourceOriginalResult.Error is not null
                || !pins.TryAdd(phase.Source, phase))
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
            }
            var original = EventVectorSourceMutationValidation.ReadObserved(phase, admission);
            EventVectorObservedPinValidation.Require(phase, original,
                phase.SourceOriginalResult.Get<EventVectorSourceResult>(), admission);
        }
        if (pins.Count != entries.Length)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        }
        var ordered = ImmutableArray.CreateBuilder<EventVectorSourcePhase>(entries.Length);
        foreach (var entry in entries)
        {
            if (!pins.TryGetValue(entry.Source, out var phase) || phase.PrincipalId != entry.PrincipalId
                || phase.SourceOwner.Incarnation != entry.Incarnation
                || phase.SourceOwner.PlacementEpoch != entry.PlacementEpoch
                || phase.SourceSchemaVersion != entry.SchemaVersion
                || phase.SourceLogicalPlacementRevision != entry.LogicalPlacementRevision
                || phase.SourceDirectoryFence != entry.DirectoryFence)
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, Invalid);
            }
            ordered.Add(phase with
            {
                SourceOriginalResult = null,
                SourceOriginalOutcomeDigest = ReadOnlyMemory<byte>.Empty,
                SourceObservationWitness = ReadOnlyMemory<byte>.Empty,
                Disposition = EventVectorSourcePhaseDisposition.Admitted
            });
        }
        return ordered.ToImmutable();
    }
}
