using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Clustering;

internal static class ResourceRetirementWorkReadShape
{
    internal static void Require(ResourceRetirementReadRequestV1 query, DatabaseLimits limits)
    {
        var fence = query.WorkFence ?? throw Invalid();
        ResourceRetirementStateValidation.Fence(fence, limits);
        if (!ResourceRetirementValidation.Same(fence.Control, query.Control)
            || !PhysicalOwnerEntryValidation.SameOwner(fence.Receipt.PhysicalOwner, query.ObservedOwner.Owner))
        {
            throw Invalid();
        }
        if (query.AcknowledgedWork is { } acknowledged)
        {
            ResourceRetirementWorkValidation.Owner(acknowledged, fence.Control, limits);
            if (!ResourceRetirementValidation.Same(acknowledged.Fence, fence)
                || !PhysicalOwnerEntryValidation.Same(acknowledged.Receiver, query.ObservedOwner))
            {
                throw Invalid();
            }
        }
    }

    private static KeyLoadException Invalid()
        => Errors.Fail(ErrorCode.Validation, ResourceRetirementReadScope.InvalidRequest);
}
