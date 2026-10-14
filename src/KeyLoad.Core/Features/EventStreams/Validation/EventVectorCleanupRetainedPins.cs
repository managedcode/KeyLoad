using System.Collections.Immutable;

namespace KeyLoad.Core;

internal static class EventVectorCleanupRetainedPins
{
    private const string Invalid = "The original retained cleanup pin union is inconsistent.";

    internal static ImmutableArray<EventVectorEncodedRow> Read(EventVectorOwnedInventory inventory,
        EventVectorAdmissionPolicy admission)
    {
        var map = inventory.Header ?? throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        var prefix = EventVectorKeys.Phases(map.ControlPartition, map.MapId);
        var phases = new List<(EventVectorEncodedRow Row, EventVectorSourcePhase Phase)>();
        foreach (var row in inventory.PayloadRows)
        {
            if (!row.Key.Span.StartsWith(prefix))
            {
                continue;
            }
            phases.Add((row, EventVectorInventoryPhaseValidation.Read(map, row, admission,
                EventVectorInventoryCoverageBound.Read(map, inventory.PayloadRows, admission))));
        }
        var pins = new List<EventVectorEncodedRow>();
        foreach (var retained in phases)
        {
            if (retained.Phase.Role != EventVectorSourcePhaseRole.Pin
                || IsReleased(retained.Phase, phases, admission))
            {
                continue;
            }
            pins.Add(retained.Row);
        }
        admission.RequireEntryCount(pins.Count);
        // The complete union uses the actual native phase key ordering, independent of supplied vector ordinals.
        pins.Sort(static (left, right) => left.Key.Span.SequenceCompareTo(right.Key.Span));
        return pins.ToImmutableArray();
    }

    internal static ReadOnlyMemory<byte> LastAdvance(EventVectorSourcePhase pin,
        EventVectorOwnedInventory inventory, EventVectorAdmissionPolicy admission)
    {
        var map = inventory.Header ?? throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        var prefix = EventVectorKeys.Phases(map.ControlPartition, map.MapId);
        EventVectorSourcePhase? latest = null;
        var bytes = ReadOnlyMemory<byte>.Empty;
        foreach (var row in inventory.PayloadRows)
        {
            if (!row.Key.Span.StartsWith(prefix))
            {
                continue;
            }
            var phase = EventVectorInventoryPhaseValidation.Read(map, row, admission,
                EventVectorInventoryCoverageBound.Read(map, inventory.PayloadRows, admission));
            if (phase.Role != EventVectorSourcePhaseRole.Advance)
            {
                continue;
            }
            var body = ReadBody(phase, admission);
            if (body.OriginalPinCommandId != pin.PhaseCommandId)
            {
                continue;
            }
            RequireOriginal(pin, phase, body);
            if (latest is not null && phase.MapRevision == latest.MapRevision
                && phase.CoverageGeneration == latest.CoverageGeneration)
            {
                throw Errors.Fail(ErrorCode.Corruption, Invalid);
            }
            if (latest is not null && (phase.MapRevision < latest.MapRevision
                || phase.MapRevision == latest.MapRevision && phase.CoverageGeneration < latest.CoverageGeneration))
            {
                continue;
            }
            latest = phase;
            bytes = row.Value;
        }
        return bytes;
    }

    private static bool IsReleased(EventVectorSourcePhase pin,
        List<(EventVectorEncodedRow Row, EventVectorSourcePhase Phase)> phases,
        EventVectorAdmissionPolicy admission)
    {
        foreach (var retained in phases)
        {
            var phase = retained.Phase;
            if (phase.Role != EventVectorSourcePhaseRole.Release
                || phase.Disposition != EventVectorSourcePhaseDisposition.Observed
                || phase.SourceOriginalResult is not { Error: null } result)
            {
                continue;
            }
            var body = EventVectorSourceMutationValidation.ReadObserved(phase, admission);
            if (body.OriginalPinCommandId != pin.PhaseCommandId)
            {
                continue;
            }
            RequireOriginal(pin, phase, body);
            var release = result.Get<EventVectorSourceReleaseResult>();
            EventVectorReleaseResultValidation.Require(phase, body, release, admission);
            if (release.Disposition == EventVectorReleaseDisposition.Released)
            {
                return true;
            }
        }
        return false;
    }

    private static EventVectorSourceMutation ReadBody(EventVectorSourcePhase phase,
        EventVectorAdmissionPolicy admission)
        => phase.Disposition == EventVectorSourcePhaseDisposition.Admitted
            ? EventVectorSourceMutationValidation.ReadOriginal(phase, admission)
            : EventVectorSourceMutationValidation.ReadObserved(phase, admission);

    private static void RequireOriginal(EventVectorSourcePhase pin, EventVectorSourcePhase phase,
        EventVectorSourceMutation body)
    {
        if (phase.MapId != pin.MapId || phase.ControlPartition != pin.ControlPartition
            || phase.ControlIncarnation != pin.ControlIncarnation || phase.Source != pin.Source
            || phase.PrincipalId != pin.PrincipalId
            || !body.OriginalPinNativeBody.Span.SequenceEqual(pin.OriginalNativeBody.Span))
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
    }
}
