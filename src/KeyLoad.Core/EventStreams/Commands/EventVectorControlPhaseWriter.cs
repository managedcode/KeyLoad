using System.Collections.Immutable;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core;

internal static class EventVectorControlPhaseWriter
{
    private const string InvalidPhase = "The original pending event vector source phase is inconsistent.";

    internal static EventVectorSourcePhase Admit(IAtomicTransaction transaction,
        EventVectorSourcePhase intended, IOptions<DatabaseLimits> options, CancellationToken cancellationToken,
        EventVectorMapState? admittedState = null, EventVectorEncodedRow? candidateProof = null)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(intended);
        ArgumentNullException.ThrowIfNull(options);
        var admission = new EventVectorAdmissionPolicy(options);
        var inventory = EventVectorMapInventory.Read(transaction, intended.ControlPartition,
            intended.MapId, options, cancellationToken);
        var header = RequireHeader(inventory, intended);
        EventVectorInventoryValidation.Require(header, inventory.PayloadRows, admission);
        var key = EventVectorKeys.Phase(header.ControlPartition, header.MapId, intended.PhaseCommandId);
        var retained = Find(inventory.PayloadRows, key);
        if (retained is { } originalRow)
        {
            var original = EventVectorInventoryPhaseValidation.Read(header, originalRow, admission,
                EventVectorInventoryCoverageBound.Read(header, inventory.PayloadRows, admission));
            RequireSameOriginal(original, intended);
            if (header.PendingPhaseId != original.PhaseCommandId
                || original.Disposition != EventVectorSourcePhaseDisposition.Admitted)
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidPhase);
            }
            return original;
        }
        if (header.PendingPhaseId is not null || intended.MapRevision != header.Revision
            || intended.Disposition != EventVectorSourcePhaseDisposition.Admitted)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidPhase);
        }
        _ = EventVectorSourceMutationValidation.ReadOriginal(intended, admission);
        var phaseRow = EventVectorEncodedRow.Create(key, intended, admission);
        var pending = header with
        {
            PendingPhaseId = intended.PhaseCommandId,
            State = admittedState ?? header.State
        };
        var payload = inventory.PayloadRows.Add(phaseRow);
        if (candidateProof is { } proof)
        {
            payload = payload.Add(proof);
        }
        EventVectorMapWriter.Write(transaction, pending, payload,
            options, cancellationToken);
        return intended;
    }

    internal static EventVectorSourcePhase Observe(IAtomicTransaction transaction, PartitionRef partition,
        Guid mapId, EventVectorSourceObservation observation, ReadOnlyMemory<byte> authenticatedReplyWitness,
        IOptions<DatabaseLimits> options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(options);
        var admission = new EventVectorAdmissionPolicy(options);
        var inventory = EventVectorMapInventory.Read(transaction, partition, mapId, options, cancellationToken);
        var header = inventory.Header ?? throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidPhase);
        EventVectorInventoryValidation.Require(header, inventory.PayloadRows, admission);
        if (header.PendingPhaseId != observation.OriginalSourcePhaseCommandId)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidPhase);
        }
        var key = EventVectorKeys.Phase(partition, mapId, observation.OriginalSourcePhaseCommandId);
        var row = Find(inventory.PayloadRows, key)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidPhase);
        var admitted = EventVectorInventoryPhaseValidation.Read(header, row, admission,
                EventVectorInventoryCoverageBound.Read(header, inventory.PayloadRows, admission));
        if (admitted.Disposition != EventVectorSourcePhaseDisposition.Admitted)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidPhase);
        }
        var observed = EventVectorObservedPhase.Create(admitted, observation, authenticatedReplyWitness, admission);
        var replacement = EventVectorEncodedRow.Create(key, observed, admission);
        var payload = inventory.PayloadRows.Select(current => current.Key.Span.SequenceEqual(key)
            ? replacement : current).ToImmutableArray();
        var settled = header with
        {
            PendingPhaseId = null,
            LastSettledPhaseId = observed.PhaseCommandId
        };
        EventVectorMapWriter.Write(transaction, settled, payload, options, cancellationToken);
        return observed;
    }

    private static EventVectorMap RequireHeader(EventVectorOwnedInventory inventory,
        EventVectorSourcePhase intended)
    {
        var header = inventory.Header ?? throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidPhase);
        if (header.Released || header.ControlIncarnation != intended.ControlIncarnation
            || header.PrincipalId != intended.PrincipalId || header.ControlPolicyEpoch != intended.ControlPolicyEpoch)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, InvalidPhase);
        }
        return header;
    }

    private static EventVectorEncodedRow? Find(ImmutableArray<EventVectorEncodedRow> rows, byte[] key)
    {
        foreach (var row in rows)
        {
            if (row.Key.Span.SequenceEqual(key))
            {
                return row;
            }
        }
        return null;
    }

    private static void RequireSameOriginal(EventVectorSourcePhase original, EventVectorSourcePhase intended)
    {
        if (!original.OriginalNativeBody.Span.SequenceEqual(intended.OriginalNativeBody.Span)
            || original.OriginalPosition != intended.OriginalPosition
            || original.ControlPolicyEpoch != intended.ControlPolicyEpoch
            || original.ControlIncarnation != intended.ControlIncarnation
            || original.MapRevision != intended.MapRevision)
        {
            throw Errors.Fail(ErrorCode.Conflict, InvalidPhase);
        }
    }
}
