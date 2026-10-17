using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class ResourceRetirementInventoryValidation
{
    internal static void Require(ResourceRetirementControlV1 control, ResourceRetirementAttemptV1 attempt,
        ResourceOwnerInventoryResultV1 inventory)
    {
        var id = attempt.OriginalCommandId;
        if (inventory.Control.Request.InventoryId != id || inventory.Control.Lifetime != control.Lifetime
            || inventory.Control.Request.TenantId != control.Request.TenantId
            || inventory.Control.Request.DatabaseId != control.Request.DatabaseId
            || inventory.Control.Request.ResourceName != control.Request.ResourceName
            || inventory.Control.Request.ExpectedIncarnation != control.Request.ExpectedIncarnation
            || inventory.Control.RetirementAttempt is not { Stage: ResourceRetirementAttemptStage.Prepared } witness
            || !ResourceRetirementAttemptValidation.SameAdmission(witness, attempt)
            || inventory.Control.Request.ExpiresAt > attempt.ExpiresAt
            || inventory.Control.PrincipalId != attempt.PrincipalId || inventory.Control.PolicyEpoch != attempt.PolicyEpoch
            || !ResourceRetirementReceiptValidation.Same(inventory.Control.Definition, control.Definition) || !ResourceRetirementReceiptValidation.Same(inventory.Control.Directory, control.Directory)
            || !ResourceRetirementReceiptValidation.Same(inventory.Control.Catalog, control.Catalog)
            || inventory.Control.PlacementDirectoryRevision != control.PlacementDirectoryRevision
            || inventory.OwnerCuts.IsDefault || inventory.Entries.IsDefault
            || inventory.OwnerCuts.Length != control.Directory.Owners.Sum(value => value.Owner.VoterIds.Length))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
        }
        var nodes = new HashSet<Guid>();
        foreach (var cut in inventory.OwnerCuts)
        {
            if (cut.NodeId == Guid.Empty || !nodes.Add(cut.NodeId))
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
            }
        }
        foreach (var owner in control.Directory.Owners)
        {
            if (inventory.OwnerCuts.Count(value => PhysicalOwnerEntryValidation.SameOwner(value.ObservedOwner, owner.Owner))
                != owner.Owner.VoterIds.Length)
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
            }
        }
    }
}
