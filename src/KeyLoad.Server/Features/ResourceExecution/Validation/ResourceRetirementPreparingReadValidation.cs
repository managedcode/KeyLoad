using KeyLoad.Clustering;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class ResourceRetirementPreparingReadValidation
{
    internal static void RequireRequest(ResourceRetirementNativeRequest outer, ResourceRetirementReadRequestV1 query,
        DatabaseLimits limits)
    {
        if (query.Control is not null || query.Attempt is not null || query.AttemptId != Guid.Empty
            || query.InventoryId != Guid.Empty || query.PageSize != 0)
        {
            throw Invalid();
        }
        var valid = query.Operation switch
        {
            ResourceRetirementReadOperation.Preparation => query.Preparation is null && query.Context is null
                && PhysicalOwnerEntryValidation.Same(outer.Control, outer.Destination),
            ResourceRetirementReadOperation.OwnerStatus => query.Preparation is not null && query.Context is null,
            _ => false
        };
        if (!valid)
        {
            throw Invalid();
        }
        if (query.Preparation is { } control)
        {
            if (!ResourceRetirementValidation.Same(control.Request, query.Request))
            {
                throw Invalid();
            }
            ResourceRetirementPreparingPhaseValidation.RequireControl(outer, control, limits);
        }
    }

    internal static void RequireReply(ResourceRetirementReadRequestV1 query, ResourceRetirementReadReplyV1 reply)
    {
        if (reply.State is not null || reply.Fence is not null || reply.InventoryControl is not null
            || reply.Attempt is not null || reply.PreparedOperation is not null)
        {
            throw Invalid();
        }
        var absent = reply.Preparation is null && reply.OwnerStatus is null;
        var valid = query.Operation switch
        {
            ResourceRetirementReadOperation.Preparation => absent || reply.Preparation is { } preparation
                && ResourceRetirementValidation.Same(preparation.Control.Request, query.Request)
                && reply.OwnerStatus is null,
            ResourceRetirementReadOperation.OwnerStatus => absent || reply.OwnerStatus is { } status
                && ResourceRetirementValidation.Same(status.Reservation.Control, query.Preparation)
                && ResourceRetirementValidation.Same(status.Reservation.Receiver, query.ObservedOwner)
                && reply.Preparation is null,
            _ => false
        };
        if (!valid)
        {
            throw Invalid();
        }
    }

    private static KeyLoadException Invalid()
        => Errors.Fail(ErrorCode.Unauthenticated, ResourceRetirementNativeProtocol.InvalidProof);
}
