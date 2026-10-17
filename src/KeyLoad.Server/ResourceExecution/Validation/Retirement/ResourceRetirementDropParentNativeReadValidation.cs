using KeyLoad.Clustering;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class ResourceRetirementDropParentNativeReadValidation
{
    internal static void RequireRequest(ResourceRetirementNativeRequest outer,
        ResourceRetirementReadRequestV1 query, DatabaseLimits limits)
    {
        ResourceRetirementDropParentReadShape.Require(query, limits);
        if (!PhysicalOwnerEntryValidation.Same(outer.Control, outer.Destination))
        {
            throw Invalid();
        }
    }

    internal static void RequireReply(ResourceRetirementReadRequestV1 query,
        ResourceRetirementReadReplyV1 reply, DatabaseLimits limits)
    {
        if (reply.State is not null || reply.Fence is not null || reply.InventoryControl is not null
            || reply.Attempt is not null || reply.PreparedOperation is not null || reply.Preparation is not null
            || reply.OwnerStatus is not null || reply.OriginalOperation is not null
            || reply.WorkJoinWitness is not null || reply.AcknowledgedOwner is not null
            || (reply.Parent is null) == (reply.AdmittedDrop is null)
            || reply.Parent is { } parent && !ResourceRetirementValidation.Same(parent.Lookup, query.ParentLookup)
            || reply.AdmittedDrop is { } admitted && (admitted.RetirementId != query.ParentLookup!.CommandId
                || admitted.TenantId != query.ParentLookup.Request.TenantId
                || admitted.DatabaseId != query.ParentLookup.Request.DatabaseId
                || admitted.ResourceName != query.ParentLookup.Request.ResourceName))
        {
            throw Invalid();
        }
        if (reply.AdmittedDrop is { } expected)
        {
            ResourceRetirementValidation.Request(expected, limits);
        }
        ResourceRetirementValidation.Bound(reply, limits);
    }

    private static KeyLoadException Invalid()
        => Errors.Fail(ErrorCode.Unauthenticated, ResourceRetirementNativeProtocol.InvalidProof);
}
