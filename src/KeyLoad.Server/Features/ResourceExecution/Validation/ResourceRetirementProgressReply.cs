using KeyLoad.Clustering;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class ResourceRetirementProgressReply
{
    internal static void Require(ResourceRetirementReadRequestV1 query, ResourceRetirementReadReplyV1 reply, DatabaseLimits limits)
    {
        if (reply.State is not null || reply.Fence is not null || reply.InventoryControl is not null
            || reply.Attempt is not null || reply.PreparedOperation is not null || reply.Preparation is not null
            || reply.OwnerStatus is not null || reply.OriginalOperation is not null || reply.WorkJoinWitness is not null
            || reply.AcknowledgedOwner is not null || reply.Parent is not null || reply.AdmittedDrop is not null)
        {
            throw Invalid();
        }
        if (query.Operation == ResourceRetirementReadOperation.OwnerRelease)
        {
            if (reply.CleanupCheckpoint is not null || reply.OwnerCleanup is not null)
            {
                throw Invalid();
            }
            if (reply.OwnerRelease is { } release)
            {
                ResourceRetirementCompletionValidation.Release(release, limits);
                Require(release.Finalization.Control, release.Receiver, query);
            }
        }
        else if (query.CleanupPartition is { } partition)
        {
            if (reply.OwnerCleanup is not null || reply.OwnerRelease is not null)
            {
                throw Invalid();
            }
            if (reply.CleanupCheckpoint is { } checkpoint)
            {
                ResourceRetirementCleanupCheckpointValidation.Require(checkpoint, limits);
                Require(checkpoint.Control, checkpoint.Receiver, query);
                if (checkpoint.Partition != partition)
                {
                    throw Invalid();
                }
            }
        }
        else
        {
            if (reply.CleanupCheckpoint is not null || reply.OwnerRelease is not null)
            {
                throw Invalid();
            }
            if (reply.OwnerCleanup is { } cleanup)
            {
                Require(cleanup.Control, cleanup.Receiver, query);
            }
        }
        ResourceRetirementValidation.Bound(reply, limits);
    }

    private static void Require(ResourceRetirementControlV1 control, RegisteredPhysicalOwnerV1 receiver,
        ResourceRetirementReadRequestV1 query)
    {
        if (!ResourceRetirementValidation.Same(control, query.Control)
            || !PhysicalOwnerEntryValidation.Same(receiver, query.ObservedOwner))
        {
            throw Invalid();
        }
    }

    private static KeyLoadException Invalid() => Errors.Fail(ErrorCode.Unauthenticated, ResourceRetirementNativeProtocol.InvalidProof);
}
