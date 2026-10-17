using KeyLoad.Clustering;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class ResourceRetirementWorkNativeReadValidation
{
    internal static void RequireRequest(ResourceRetirementNativeRequest outer,
        ResourceRetirementReadRequestV1 query, DatabaseLimits limits)
    {
        ResourceRetirementReadActionShape.Require(query, limits);
        ResourceRetirementNativePhaseValidation.RequireControl(outer, query.Control!, limits);
        if (!PhysicalOwnerEntryValidation.SameOwner(query.WorkFence!.Receipt.PhysicalOwner, outer.Destination.Owner))
        {
            throw Invalid();
        }
    }

    internal static void RequireReply(ResourceRetirementReadRequestV1 query,
        ResourceRetirementReadReplyV1 reply, DatabaseLimits limits)
    {
        if (reply.State is not null || reply.Fence is not null || reply.InventoryControl is not null
            || reply.Attempt is not null || reply.PreparedOperation is not null || reply.Preparation is not null
            || reply.OwnerStatus is not null || reply.OriginalOperation is not null || reply.WorkJoinWitness is not { } witness)
        {
            throw Invalid();
        }
        if (!ResourceRetirementValidation.Same(witness.Fence, query.WorkFence)
            || !PhysicalOwnerEntryValidation.SameOwner(witness.ActualOwner, query.ObservedOwner.Owner)
            || witness.VoterId != query.DestinationVoter)
        {
            throw Invalid();
        }
        if (reply.AcknowledgedOwner is { } durable)
        {
            ResourceRetirementWorkValidation.Owner(durable, query.Control!, limits);
            if (!ResourceRetirementValidation.Same(durable.Fence, query.WorkFence)
                || !durable.Evidence.Any(item => ResourceRetirementValidation.Same(item.Witness, witness)))
            {
                throw Invalid();
            }
        }
        else if (query.AcknowledgedWork is not null)
        {
            throw Invalid();
        }
        if (reply.AcknowledgedOwner is null && witness.FullSiloAddress != query.DestinationSiloAddress)
        {
            throw Invalid();
        }
        ResourceRetirementValidation.Bound(reply, limits);
    }

    private static KeyLoadException Invalid()
        => Errors.Fail(ErrorCode.Unauthenticated, ResourceRetirementNativeProtocol.InvalidProof);
}
