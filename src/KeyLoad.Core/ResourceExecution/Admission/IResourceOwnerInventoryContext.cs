using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Core.Features.ResourceExecution;

internal interface IResourceOwnerInventoryContext
{
    void UseVerifiedImage(PartitionRef partition);

    void UseControlledResources(PartitionRef partition, ImmutableArray<ResourceDefinition> definitions,
        ResourceLifetimeCoverageV1 coverage);
}
