using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class ControlledRetireAspirePublicIds
{
    internal static Guid For(PartitionMoveRequest request, long generation)
        => PartitionMovementParentPhaseIds.For(request, PartitionMovementPublicParentRf3Administrator.PrincipalId,
            PartitionMovementParentPhaseRole.Retire, cleanupGeneration: generation);
}
