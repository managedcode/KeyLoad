using System.Collections.Immutable;
using KeyLoad.Core.Features.CommandOutcomes.Contracts;
using KeyLoad.Core.Features.RuntimeJournaling.Contracts;
using KeyLoad.Core.Features.StoragePartitions.Contracts;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Associates exact authenticated resource lifetimes with the explicitly supplied original work owner.</summary>
internal interface IResourceLifetimeWorkBinding
{
    ResourceLifetimeWorkResultRetention ResultRetention
    {
        get;
    }

    void Bind(ResourceLifetimeCoverage coverage);

    void Bind(ImmutableArray<ResourceLifetime> actualLifetimes);
}
