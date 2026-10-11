using System.Collections.Immutable;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core;

internal static class EventVectorControlObserve
{
    private const string InvalidObservation = "The original admitted event vector observation is inconsistent.";

    internal static EventVectorSourcePhase Execute(IAtomicTransaction transaction,
        EventVectorControlPhase control, IOptions<DatabaseLimits> options,
        CancellationToken cancellationToken)
    {
        var admission = new EventVectorAdmissionPolicy(options);
        var observed = control.ObservedSourcePhase
            ?? throw Errors.Fail(ErrorCode.Corruption, InvalidObservation);
        _ = EventVectorSourceMutationValidation.ReadObserved(observed, admission);
        var request = control.OriginalRequest;
        var inventory = EventVectorMapInventory.Read(transaction, request.ControlPartition,
            request.MapId, options, cancellationToken);
        var map = inventory.Header ?? throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidObservation);
        EventVectorInventoryValidation.Require(map, inventory.PayloadRows, admission);
        if (control.Action != EventVectorControlAction.ObserveSource
            || map.PendingPhaseId != observed.PhaseCommandId
            || control.ExpectedPendingSourcePhaseId != observed.PhaseCommandId)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidObservation); }
        var key = EventVectorKeys.Phase(map.ControlPartition, map.MapId, observed.PhaseCommandId);
        var original = ReadOriginal(map, inventory.PayloadRows, key, admission);
        if (original.Disposition != EventVectorSourcePhaseDisposition.Admitted
            || !original.OriginalNativeBody.Span.SequenceEqual(observed.OriginalNativeBody.Span)
            || original.OriginalPosition != observed.OriginalPosition
            || original.ControlPolicyEpoch != observed.ControlPolicyEpoch
            || original.ControlIncarnation != observed.ControlIncarnation
            || original.MapRevision != observed.MapRevision)
        { throw Errors.Fail(ErrorCode.Conflict, InvalidObservation); }
        var replacement = EventVectorEncodedRow.Create(key, observed, admission);
        var payload = inventory.PayloadRows.Select(row => row.Key.Span.SequenceEqual(key)
            ? replacement : row).ToImmutableArray();
        EventVectorMapWriter.Write(transaction, map with
        { PendingPhaseId = null, LastSettledPhaseId = observed.PhaseCommandId },
            payload, options, cancellationToken);
        return observed;
    }

    private static EventVectorSourcePhase ReadOriginal(EventVectorMap map,
        ImmutableArray<EventVectorEncodedRow> rows, byte[] key, EventVectorAdmissionPolicy admission)
    {
        foreach (var row in rows)
        {
            if (row.Key.Span.SequenceEqual(key))
            {
                return EventVectorInventoryPhaseValidation.Read(map, row, admission,
                EventVectorInventoryCoverageBound.Read(map, rows, admission));
            }
        }
        throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidObservation);
    }
}
