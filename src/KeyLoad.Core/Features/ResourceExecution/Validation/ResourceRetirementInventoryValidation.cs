using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Validates bounded authenticated all-voter inventory witnesses; transport owns voter identity authentication.</summary>
internal static class ResourceRetirementInventoryValidation
{
    internal static void Require(ResourceOwnerInventoryResultV1 inventory, ResourceRetirementControlV1 control,
        DatabaseLimits limits)
    {
        if (inventory is null || !inventory.Complete
            || inventory.CoverageKind != ResourceOwnerInventoryCoverageKind.ConservativeMayContain
            || inventory.OwnerCuts.IsDefaultOrEmpty || inventory.Entries.IsDefault
            || inventory.OwnerCuts.Length > limits.MaxResults || inventory.Entries.Length > limits.MaxResults)
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Incomplete);
        }
        ResourceOwnerInventoryReadValidation.Control(inventory.Control, limits);
        ResourceRetirementAttemptValidation.Require(inventory.Control.RetirementAttempt!, control, limits);
        if (inventory.Control.RetirementAttempt!.Stage != ResourceRetirementAttemptStage.Prepared)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementProtocol.Changed);
        }
        SameSource(inventory.Control, control);
        var nodes = new HashSet<Guid>();
        foreach (var cut in inventory.OwnerCuts)
        {
            ResourceOwnerInventoryReadValidation.OwnerCut(cut, limits);
            var member = ResourceRetirementStateValidation.Member(control, cut.ObservedOwner);
            if (cut.StoreIncarnation != member.Owner.Incarnation || !nodes.Add(cut.NodeId))
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, ResourceRetirementProtocol.Changed);
            }
        }
        foreach (var owner in control.Directory.Owners)
        {
            if (inventory.OwnerCuts.Count(cut => PhysicalOwnerEntryValidation.SameOwner(cut.ObservedOwner, owner.Owner))
                != owner.Owner.VoterIds.Length)
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Incomplete);
            }
        }
        var partitions = new HashSet<(PartitionRef, Guid)>();
        foreach (var entry in inventory.Entries)
        {
            Entry(entry, control, partitions);
        }
        ResourceRetirementValidation.Bound(inventory, limits);
    }

    internal static void SameSource(ResourceOwnerInventoryControlV1 inventory, ResourceRetirementControlV1 control)
    {
        if (!ResourceRetirementValidation.Same(inventory.Definition, control.Definition)
            || !ResourceRetirementValidation.Same(inventory.Lifetime, control.Lifetime)
            || !ResourceRetirementValidation.Same(inventory.Catalog, control.Catalog)
            || !ResourceRetirementValidation.Same(inventory.Directory, control.Directory)
            || inventory.PlacementDirectoryRevision != control.PlacementDirectoryRevision
            || inventory.RetirementAttempt is null
            || inventory.Request.InventoryId != inventory.RetirementAttempt.OriginalCommandId
            || inventory.Request.ExpiresAt > inventory.RetirementAttempt.ExpiresAt)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ResourceRetirementProtocol.Changed);
        }
    }

    private static void Entry(ResourceOwnerInventoryEntryV1 entry, ResourceRetirementControlV1 control,
        HashSet<(PartitionRef, Guid)> partitions)
    {
        if (entry is null || entry.Partition is null || entry.CurrentPlacement is null || entry.CurrentRoute is null
            || !ResourceRetirementValidation.Same(entry.Lifetime, control.Lifetime)
            || entry.Partition.TenantId != control.Lifetime.TenantId
            || entry.Partition.DatabaseId != control.Lifetime.DatabaseId
            || entry.Partition.TransactionDomainId != control.Lifetime.TransactionDomainId
            || entry.CurrentPlacement.VoterIds.IsDefaultOrEmpty)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        DatabaseEngine.ValidatePartition(entry.Partition);
        ResourceRetirementStateValidation.Member(control, entry.ObservedOwner);
        var route = ResourceRetirementStateValidation.Member(control, entry.CurrentRoute.Owner);
        if (!PhysicalOwnerEntryValidation.Same(route, entry.CurrentRoute)
            || entry.CurrentPlacement.PhysicalShardId != route.Owner.PhysicalShardId
            || entry.CurrentPlacement.Incarnation != route.Owner.Incarnation
            || !entry.CurrentPlacement.VoterIds.SequenceEqual(route.Owner.VoterIds, StringComparer.Ordinal)
            || entry.CurrentPlacement.PlacementEpoch < route.Owner.PlacementEpoch
            || !partitions.Add((entry.Partition, entry.ObservedOwner.PhysicalShardId)))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ResourceRetirementProtocol.Changed);
        }
    }
}
