using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

internal static class ResourceRetirementCleanupStorage
{
    internal static byte[] Key(ResourceRetirementControlV1 control, RegisteredPhysicalOwnerV1 receiver,
        PartitionRef partition)
        => KeyCodec.Encode("resource-retirement-cleanup-checkpoint-v1", control.Lifetime.TenantId,
            control.Lifetime.DatabaseId, control.Lifetime.ResourceName, control.Lifetime.Incarnation.Value,
            receiver.Owner.PhysicalShardId, receiver.Owner.Incarnation, partition.TransactionDomainId,
            partition.PartitionKey);

    internal static ResourceRetirementCleanupCheckpointV1? Read(IKeyValueView view,
        ResourceRetirementControlV1 control, RegisteredPhysicalOwnerV1 receiver, PartitionRef partition,
        DatabaseLimits limits)
    {
        var stored = ResourceRetirementStorage.Read<ResourceRetirementCleanupCheckpointV1>(view,
            Key(control, receiver, partition), limits);
        if (stored is not null && (!ResourceRetirementValidation.Same(stored.Control, control)
            || !ResourceRetirementValidation.Same(stored.Receiver, receiver) || stored.Partition != partition
            || stored.Ordinal <= 0 || stored.FamilyOrdinal < 0
            || stored.FamilyOrdinal > ResourceRetirementTableCleanupFamilies.Count
            || stored.Complete != (stored.FamilyOrdinal == ResourceRetirementTableCleanupFamilies.Count)
            || stored.Complete && !stored.AfterKey.IsEmpty))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        if (stored is not null)
        {
            ResourceRetirementCleanupCheckpointValidation.Require(stored, limits);
        }
        return stored;
    }
}
