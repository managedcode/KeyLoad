using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string ForeignPlacementExecution = "The atomic partition is owned by another physical shard.";
    private const string UnregisteredPlacementOwner = "The physical shard is not registered for this partition.";
    private const string OwnerDirectoryControlMismatch = "The committed physical owner directory does not match its control shard.";

    private static PhysicalShardRecord ResolveRegisteredPlacementOwner(IKeyValueView view,
        PhysicalShardRecord control, Guid selectedOwner, ErrorCode missingOwnerCode,
        ReadExecutionBudgetReadGrant? grant = null)
    {
        if (selectedOwner == control.PhysicalShardId)
        { return control; }
        var owners = (grant is null ? PhysicalOwnerDirectorySerialization.Read(view)
            : PhysicalOwnerDirectorySerialization.Read(view, grant))
            ?? throw Errors.Fail(missingOwnerCode, UnregisteredPlacementOwner);
        if (!PhysicalOwnerEntryValidation.SameOwner(owners.ControlOwner, control))
        { throw Errors.Fail(ErrorCode.Corruption, OwnerDirectoryControlMismatch); }
        var selected = owners.Owners.SingleOrDefault(entry => entry.Owner.PhysicalShardId == selectedOwner);
        return selected?.Owner ?? throw Errors.Fail(missingOwnerCode, UnregisteredPlacementOwner);
    }

    private static AtomicPartitionPlacementResolution ResolveRegisteredPlacement(IKeyValueView view,
        PartitionRef partition, PhysicalShardRecord control)
    {
        var row = AtomicPartitionPlacementSerialization.ReadRow(view, partition);
        var owner = row is null ? control
            : ResolveRegisteredPlacementOwner(view, control, row.PhysicalShardId, ErrorCode.Corruption);
        var directory = AtomicPartitionPlacementSerialization.ReadDirectory(view);
        owner = ResolveMovementPlacementOwner(view, partition, owner, row);
        return ResolvePlacement(partition, owner, directory, row);
    }
}
