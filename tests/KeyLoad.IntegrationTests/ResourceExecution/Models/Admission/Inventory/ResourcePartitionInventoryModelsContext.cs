using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.DatabaseComposition;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class ResourcePartitionInventoryModelsContext
{
    internal static FunctionalGateContext Create(RequestCqrsRf3Callers callers, int voters)
        => new(callers, callers, callers, ResourcePartitionInventoryModelsProtocol.Partition,
            ResourcePartitionInventoryModelsProtocol.Anchor, ResourcePartitionInventoryModelsProtocol.PerFamily)
        {
            QueueAdministrator = callers,
            WriterPrincipalId = PartitionMovementPublicParentRf3Administrator.PrincipalId,
            ReaderPrincipalId = PartitionMovementPublicParentRf3Administrator.PrincipalId,
            VoterCount = voters
        };
}
