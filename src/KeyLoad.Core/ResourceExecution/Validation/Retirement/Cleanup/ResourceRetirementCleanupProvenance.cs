using KeyLoad.Core.Features.BackupRestore.Serialization;
using KeyLoad.Core.Features.BackupRestore.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

internal static class ResourceRetirementCleanupProvenance
{
    private const long NoAppliedIndex = 0;

    internal static void Require(IKeyValueView view, ResourceLifetimeV1 lifetime, PartitionRef partition,
        Guid currentIncarnation, long storePosition, DatabaseLimits limits)
    {
        var origin = ResourceOwnerInventoryCatalog.ReadOrigin(view, limits);
        if (origin is null || !origin.Proven || origin.StoreIncarnation != currentIncarnation)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceOwnerInventoryProtocol.Unknown);
        }
        var key = ResourceOwnerInventoryKeys.Entry(lifetime, partition);
        var bytes = view.ReadOwnedValue(key)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceOwnerInventoryProtocol.Unknown);
        var reverse = ResourceOwnerInventoryCatalog.ReadEntry(key, bytes, lifetime, limits);
        if (reverse.Partition != partition)
        {
            throw ResourceOwnerInventoryValidation.Invalid();
        }

        var memberKey = ResourceOwnerInventoryKeys.Partition(reverse);
        var memberBytes = view.ReadOwnedValue(memberKey)
            ?? throw ResourceOwnerInventoryValidation.Invalid();
        var member = ResourceOwnerInventoryCatalog.ReadPartitionEntry(memberKey, memberBytes, partition, limits);
        ResourceOwnerInventoryValidation.Enrollment(member, requireProven: true);
        if (member.Lifetime != lifetime)
        {
            throw ResourceOwnerInventoryValidation.Invalid();
        }

        RequireRoster(view, partition, currentIncarnation, storePosition);
    }

    private static void RequireRoster(IKeyValueView view, PartitionRef partition, Guid incarnation, long storePosition)
    {
        var bytes = view.ReadOwnedValue(AtomicPartitionRosterKeys.Partition(partition))
            ?? throw ResourceOwnerInventoryValidation.Invalid();
        var entry = NativeSerialization.Deserialize<AtomicPartitionCatalogEntryV1>(bytes)
            ?? throw ResourceOwnerInventoryValidation.Invalid();
        var historicalBound = AtomicPartitionRosterOriginValidation.ReadBound(view, entry, bytes, incarnation);
        var appliedBytes = view.ReadOwnedValue(KeySpace.AppliedBytes);
        var applied = appliedBytes is null ? NoAppliedIndex : NativeSerialization.Deserialize<long>(appliedBytes);
        AtomicPartitionRosterEntryValidation.Validate(entry, partition, storePosition, Math.Max(applied, historicalBound));
    }
}
