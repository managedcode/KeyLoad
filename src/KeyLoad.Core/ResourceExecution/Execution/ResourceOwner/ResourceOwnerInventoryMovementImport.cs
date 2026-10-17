using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

internal static class ResourceOwnerInventoryMovementImport
{
    internal static void Begin(IAtomicTransaction transaction, PartitionRef partition, Guid moveId,
        DatabaseLimits limits)
    {
        var key = ResourceOwnerInventoryMovementRows.ImportKey(partition, moveId);
        var previous = ResourceOwnerInventoryMovementRows.Read<ResourceOwnerInventoryImportOriginV1>(transaction, key, limits);
        if (previous is null)
        {
            var origin = ResourceOwnerInventoryCatalog.ReadOrigin(transaction, limits);
            var checkpoint = new ResourceOwnerInventoryImportOriginV1(ResourceOwnerInventoryMovementProtocol.Version,
                moveId, partition, origin);
            ResourceOwnerInventoryMovementRows.Write(transaction, key, checkpoint, limits);
        }
        else
        {
            RequireCheckpoint(previous, partition, moveId);
        }
        // The persisted checkpoint fences inventory reads; normal writes keep enrolling.
        // Unknown is never promoted after this multi-command import interval.
    }

    internal static void Abort(IAtomicTransaction transaction, PartitionRef partition, Guid moveId,
        DatabaseLimits limits)
    {
        var key = ResourceOwnerInventoryMovementRows.ImportKey(partition, moveId);
        var checkpoint = ResourceOwnerInventoryMovementRows.Read<ResourceOwnerInventoryImportOriginV1>(transaction, key, limits);
        if (checkpoint is null)
        {
            return;
        }
        RequireCheckpoint(checkpoint, partition, moveId);
        transaction.Delete(key);
    }

    internal static bool TryInstall(IAtomicTransaction transaction, PartitionMoveImagePage page,
        KeyValueRecord record, DatabaseLimits limits)
    {
        if (page.Family != PartitionRecordFamilies.ResourceOwnerInventoryMember)
        {
            return false;
        }
        var entry = ResourceOwnerInventoryCatalog.ReadPartitionEntry(record.Key.Span, record.Value.Span,
            page.Partition, limits);
        if (entry.Origin.Proven)
        {
            ResourceOwnerInventoryCatalog.Import(transaction, entry, limits);
        }
        else
        {
            transaction.Put(record.Key.ToArray(), record.Value.ToArray());
        }
        return true;
    }

    internal static void Complete(IAtomicTransaction transaction, PartitionMoveTargetStage stage,
        StoreIdentity identity, DatabaseLimits limits)
    {
        Begin(transaction, stage.Control.Partition, stage.Control.MoveId, limits);
        var partition = stage.Control.Partition;
        var checkpoint = ResourceOwnerInventoryMovementRows.Read<ResourceOwnerInventoryImportOriginV1>(transaction,
            ResourceOwnerInventoryMovementRows.ImportKey(partition, stage.Control.MoveId), limits)
            ?? throw Errors.Fail(ErrorCode.Corruption, ResourceOwnerInventoryMovementProtocol.Invalid);
        RequireCheckpoint(checkpoint, partition, stage.Control.MoveId);
        var proof = ResourceOwnerInventoryMovementRows.Read<ResourceOwnerInventoryMovementProofV1>(transaction,
            ResourceOwnerInventoryMovementRows.ProofKey(partition), limits);
        if (proof is not null)
        {
            ResourceOwnerInventoryMovementProofValidation.Require(proof, stage, limits);
        }
        var current = ResourceOwnerInventoryCatalog.ReadOrigin(transaction, limits);
        if (current is { Proven: true } && current.StoreIncarnation == identity.Incarnation
            && checkpoint.PriorOrigin is { Proven: true } prior && prior.StoreIncarnation == identity.Incarnation
            && proof?.Origin is { Proven: true } source
            && source.StoreIncarnation == stage.Fence.SourcePlacement.Incarnation)
        {
            ResourceOwnerInventoryCatalog.RestoreVerified(transaction, identity, prior, limits);
        }
        else
        {
            ResourceOwnerInventoryCatalog.Invalidate(transaction, limits);
        }
        transaction.Delete(ResourceOwnerInventoryMovementRows.ImportKey(partition, stage.Control.MoveId));
    }

    private static void RequireCheckpoint(ResourceOwnerInventoryImportOriginV1 value, PartitionRef partition, Guid moveId)
    {
        if (value.Version != ResourceOwnerInventoryMovementProtocol.Version || value.Partition != partition
            || value.MoveId != moveId)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceOwnerInventoryMovementProtocol.Invalid);
        }
    }
}
