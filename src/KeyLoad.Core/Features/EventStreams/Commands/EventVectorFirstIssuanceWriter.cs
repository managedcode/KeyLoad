using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

internal static class EventVectorFirstIssuanceWriter
{
    private const long NewSlot = 1;
    private const long ExistingSlot = 0;
    private const long EmptyBytes = 0;
    private const string Invalid = "The original event vector first issuance ownership is inconsistent.";

    internal static void Create(IAtomicTransaction transaction, EventVectorFirstIssuance proposed,
        EventVectorAdmissionPolicy admission)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(proposed);
        ArgumentNullException.ThrowIfNull(admission);
        var key = EventVectorKeys.FirstIssuance(proposed.ControlPartition, proposed.PrincipalId,
            proposed.OriginalPublicCommandId);
        if (EventVectorRowStorage.Read<EventVectorFirstIssuance>(transaction, key, admission) is not null
            || EventVectorRowStorage.Read<EventVectorFirstIssuance>(transaction,
                EventVectorKeys.TerminalFirstIssuance(proposed.ControlPartition, proposed.PrincipalId,
                    proposed.OriginalPublicCommandId), admission) is not null)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        }
        if (proposed.State != EventVectorFirstIssuanceState.Admitted)
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
        var row = EventVectorEncodedRow.Create(key, proposed, admission);
        var capacity = EventVectorCapacityLedger.Prepare(transaction, proposed.ControlPartition,
            NewSlot, row.EncodedBytes, admission);
        EventVectorRowStorage.Write(transaction, row);
        EventVectorRowStorage.Write(transaction, capacity);
    }

    internal static void Replace(IAtomicTransaction transaction, EventVectorFirstIssuance original,
        EventVectorFirstIssuance proposed, EventVectorAdmissionPolicy admission)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(proposed);
        ArgumentNullException.ThrowIfNull(admission);
        RequireOriginal(original, proposed);
        if (original.State == EventVectorFirstIssuanceState.Released
            || proposed.State == EventVectorFirstIssuanceState.Released)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        }
        var key = EventVectorKeys.FirstIssuance(original.ControlPartition, original.PrincipalId,
            original.OriginalPublicCommandId);
        var previousBytes = ReadOriginalBytes(transaction, key, admission);
        var changed = EventVectorEncodedRow.Create(key, proposed, admission);
        var capacity = EventVectorCapacityLedger.Prepare(transaction, original.ControlPartition,
            ExistingSlot, checked(changed.EncodedBytes - previousBytes), admission);
        EventVectorRowStorage.Write(transaction, changed);
        EventVectorRowStorage.Write(transaction, capacity);
    }

    internal static void TransferToMap(IAtomicTransaction transaction, EventVectorFirstIssuance original,
        EventVectorAdmissionPolicy admission)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(admission);
        if (original.State != EventVectorFirstIssuanceState.DispatchReserved)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        }
        var key = EventVectorKeys.FirstIssuance(original.ControlPartition, original.PrincipalId,
            original.OriginalPublicCommandId);
        var originalBytes = ReadOriginalBytes(transaction, key, admission);
        var capacity = EventVectorCapacityLedger.Prepare(transaction, original.ControlPartition,
            -NewSlot, -originalBytes, admission);
        transaction.Delete(key);
        EventVectorRowStorage.Write(transaction, capacity);
    }

    internal static EventVectorFirstIssuance Release(IAtomicTransaction transaction,
        EventVectorFirstIssuance original, ReplicatedOperation actualCleanup,
        EventVectorAdmissionPolicy admission)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(actualCleanup);
        ArgumentNullException.ThrowIfNull(admission);
        var phase = NativeCommandPayload.Read<EventVectorControlPhase>(actualCleanup);
        if (actualCleanup.Kind != OperationKind.EventFeedControl || actualCleanup.PrincipalId != original.PrincipalId
            || phase.Action != EventVectorControlAction.CleanupAbandonedIssuance
            || phase.OriginalFirstIssuancePublicCommandId != original.OriginalPublicCommandId
            || phase.OriginalRequest.ControlPartition != original.ControlPartition
            || phase.OriginalRequest.MapId != original.MapId)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, Invalid);
        }
        if (original.State != EventVectorFirstIssuanceState.Abandoned
            || actualCleanup.EvaluatedAt < original.OriginalParentExpiresAt)
        {
            throw Errors.Fail(ErrorCode.CursorExpired, Invalid);
        }
        var activeKey = EventVectorKeys.FirstIssuance(original.ControlPartition, original.PrincipalId,
            original.OriginalPublicCommandId);
        var terminalKey = EventVectorKeys.TerminalFirstIssuance(original.ControlPartition, original.PrincipalId,
            original.OriginalPublicCommandId);
        if (EventVectorRowStorage.Read<EventVectorFirstIssuance>(transaction, terminalKey, admission) is not null)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        }
        var released = original with
        {
            State = EventVectorFirstIssuanceState.Released,
            LastCleanupNativeCommandId = actualCleanup.Id,
            LastCleanupNativePayloadBytes = actualCleanup.NativePayload.ToArray(),
            LastCleanupNativeFingerprint = NativeOperationFingerprint.Compute(actualCleanup)
        };
        var terminal = EventVectorEncodedRow.Create(terminalKey, released, admission);
        var originalBytes = ReadOriginalBytes(transaction, activeKey, admission);
        var capacity = EventVectorCapacityLedger.Prepare(transaction, original.ControlPartition,
            -NewSlot, -originalBytes, admission);
        transaction.Delete(activeKey);
        EventVectorRowStorage.Write(transaction, terminal);
        EventVectorRowStorage.Write(transaction, capacity);
        return released;
    }

    private static long ReadOriginalBytes(IKeyValueView view, byte[] key, EventVectorAdmissionPolicy admission)
    {
        var originalBytes = EmptyBytes;
        var found = view.ReadValue(key, value =>
        {
            originalBytes = checked((long)key.Length + value.Length);
            admission.RequireEncodedBytes(originalBytes);
        });
        if (!found)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        }
        return originalBytes;
    }

    private static void RequireOriginal(EventVectorFirstIssuance original, EventVectorFirstIssuance proposed)
    {
        if (original.Version != proposed.Version || original.PrincipalId != proposed.PrincipalId
            || original.OriginalPublicCommandId != proposed.OriginalPublicCommandId
            || original.ControlPartition != proposed.ControlPartition || original.MapId != proposed.MapId
            || original.OriginalParentExpiresAt != proposed.OriginalParentExpiresAt
            || !original.OriginalPublicRequestBytes.Span.SequenceEqual(proposed.OriginalPublicRequestBytes.Span)
            || !original.OriginalPublicRequestDigest.Span.SequenceEqual(proposed.OriginalPublicRequestDigest.Span)
            || !original.OriginalAuthorizedSourceScopes.SequenceEqual(proposed.OriginalAuthorizedSourceScopes)
            || !original.OriginalCoverageWitnessBytes.Span.SequenceEqual(proposed.OriginalCoverageWitnessBytes.Span)
            || !original.OriginalCapturedFirstOpenPhaseBytes.IsEmpty
                && !original.OriginalCapturedFirstOpenPhaseBytes.Span.SequenceEqual(
                    proposed.OriginalCapturedFirstOpenPhaseBytes.Span))
        {
            throw Errors.Fail(ErrorCode.Conflict, Invalid);
        }
    }
}
