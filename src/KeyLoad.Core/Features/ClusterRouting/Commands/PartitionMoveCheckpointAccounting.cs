using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private PartitionMoveParentHeader SaveMoveParentAdmission(IAtomicTransaction transaction,
        PartitionMoveParentHeader? previous, PartitionMoveParentHeader header, PartitionMoveParentPhase admitted)
    {
        if (admitted.Stage == PartitionMovePeerStage.Capture
            && header.OriginalCapturePhaseCommandId is { } captureId
            && captureId != admitted.OriginalPhaseCommandId)
        { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
        var phaseBytes = checked((long)PartitionMoveParentKeys.Phase(header.Partition, header.MoveId,
            admitted.OriginalPhaseCommandId).Length + NativeSerialization.Measure(admitted));
        var infrastructureBytes = previous is null
            ? checked(CheckpointStagedBytes(transaction, PartitionMoveParentKeys.Active(header.Partition))
                + CheckpointStagedBytes(transaction, PartitionMoveParentKeys.PrincipalActive(header.OperatorPrincipalId))
                + CheckpointStagedBytes(transaction, PartitionMoveParentKeys.DatabaseActive(header.Partition))) : PartitionMoveProtocol.EmptyCount;
        var oldHeaderBytes = previous is null ? PartitionMoveProtocol.EmptyCount : NativeSerialization.Measure(previous);
        var baseBytes = checked((previous?.RetainedMetadataBytes ?? PartitionMoveProtocol.EmptyCount) + phaseBytes + infrastructureBytes
            + (previous is null ? PartitionMoveParentKeys.Header(header.Partition, header.MoveId).Length : PartitionMoveProtocol.EmptyCount)
            - oldHeaderBytes);
        var updated = header with
        {
            Generation = checked(header.Generation + PartitionMoveProtocol.SequenceStep),
            RetainedPhaseCount = checked(header.RetainedPhaseCount + PartitionMoveProtocol.SequenceStep),
            InterruptedOriginalPhaseCommandId = header.PendingOriginalPhaseCommandId
                ?? header.InterruptedOriginalPhaseCommandId,
            PendingOriginalPhaseCommandId = admitted.OriginalPhaseCommandId,
            LastOriginalPhaseCommandId = admitted.OriginalPhaseCommandId,
            OriginalCapturePhaseCommandId = admitted.Stage == PartitionMovePeerStage.Capture
                ? admitted.OriginalPhaseCommandId : header.OriginalCapturePhaseCommandId
        };
        updated = MeasureMoveParentHeader(updated, baseBytes);
        if (updated.RetainedPhaseCount > movementCheckpoints.MaxPhaseRecordsPerMove
            || updated.RetainedMetadataBytes > movementCheckpoints.MaxRetainedMetadataBytesPerMove)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
        var payloadBytes = checked(phaseBytes + PartitionMoveParentKeys.Header(header.Partition, header.MoveId).Length
            + NativeSerialization.Measure(updated));
        payloadBytes = checked(payloadBytes + infrastructureBytes);
        if (payloadBytes > Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
        PartitionMoveParentStorage.WritePhase(transaction, admitted, Limits.MaxBatchBytes);
        PartitionMoveParentStorage.WriteHeader(transaction, updated, Limits.MaxBatchBytes);
        return updated;
    }

    private static PartitionMoveParentHeader MeasureMoveParentHeader(PartitionMoveParentHeader header, long baseBytes)
    {
        // The byte-count field may change its own generated integer encoding width, bounded by one native long.
        for (var width = PartitionMoveProtocol.EmptyCount; width <= sizeof(long); width++)
        {
            var measured = checked(baseBytes + NativeSerialization.Measure(header));
            if (measured == header.RetainedMetadataBytes)
            { return header; }
            header = header with { RetainedMetadataBytes = measured };
        }
        throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
    }

    private static long CheckpointStagedBytes(IKeyValueView view, byte[] key)
    {
        var value = view.ReadOwnedValue(key)
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
        return checked((long)key.Length + value.Length);
    }
}
