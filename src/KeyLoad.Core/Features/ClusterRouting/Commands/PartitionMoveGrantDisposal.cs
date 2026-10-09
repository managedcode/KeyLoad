using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const int MoveAbortMetadataMutations = 6;
    private const int MoveAbortGrantMutations = 2;

    private bool DisposeAbortedMoveGrants(IAtomicTransaction transaction, PartitionMoveControlRecord control,
        PartitionMoveJournalReceipt source, PartitionMoveJournalReceipt target)
    {
        var maximum = (Limits.MaxBatchMutations - MoveAbortMetadataMutations) / MoveAbortGrantMutations;
        if (maximum <= PartitionMoveProtocol.EmptyCount)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
        var changed = PartitionMoveProtocol.EmptyCount;
        long bytes = PartitionMoveProtocol.EmptyCount;
        var pending = false;
        foreach (var row in ReadMoveGrantIndex(transaction, control))
        {
            var grant = ReadIndexedMoveGrant(transaction, control, row);
            if (grant.RetireCancellationDisposition is not null)
            {
                RequireRetireGrantCancellationDisposition(transaction, control, grant);
                continue;
            }
            if (grant.Settlement is not null || grant.AbortDisposition is not null)
            { continue; }
            var barrier = grant.ReceiverOwner.Incarnation == control.SourcePlacement.Incarnation ? source : target;
            if (!PhysicalOwnerEntryValidation.SameOwner(barrier.PhysicalOwner, grant.ReceiverOwner))
            { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
            var disposed = grant with { AbortDisposition = barrier };
            var measured = NativeSerialization.Measure(disposed);
            if (changed >= maximum || bytes + measured > Limits.MaxBatchBytes)
            { pending = true; continue; }
            bytes = checked(bytes + measured);
            PartitionMoveGrantStorage.Write(transaction, disposed, Limits.MaxBatchBytes);
            PartitionMoveGrantStorage.ChangeOutstanding(transaction,
                PartitionMoveGrantStorage.MoveCountKey(control.Partition, control.MoveId), false, Limits.MaxBatchMutations);
            PartitionMoveGrantStorage.ChangeOutstanding(transaction, grant.OperatorPrincipalId, false, Limits.MaxBatchMutations);
            PartitionMoveGrantStorage.ChangeOutstanding(transaction,
                PartitionMoveGrantStorage.DatabaseKey(control.Partition.TenantId, control.Partition.DatabaseId),
                false, Limits.MaxBatchMutations);
            changed++;
        }
        if (pending && changed == PartitionMoveProtocol.EmptyCount)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
        return !pending;
    }

    private void RequireClosedRetireGrantIntegrity(IKeyValueView view, PartitionMoveControlRecord control)
    {
        foreach (var row in ReadMoveGrantIndex(view, control))
        {
            var grant = ReadIndexedMoveGrant(view, control, row);
            if (grant.RetireCancellationDisposition is not null)
            { RequireRetireGrantCancellationDisposition(view, control, grant); }
        }
    }

    private void RequireNoUnsettledMoveGrants(IKeyValueView view, PartitionMoveControlRecord control)
    {
        if (PartitionMoveGrantStorage.Outstanding(view,
            PartitionMoveGrantStorage.MoveCountKey(control.Partition, control.MoveId)) != PartitionMoveProtocol.EmptyCount)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        foreach (var row in ReadMoveGrantIndex(view, control))
        {
            var grant = ReadIndexedMoveGrant(view, control, row);
            if (grant.RetireCancellationDisposition is not null)
            {
                RequireRetireGrantCancellationDisposition(view, control, grant);
                continue;
            }
            if (grant.Settlement is null && grant.AbortDisposition is null)
            { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        }
    }
}
