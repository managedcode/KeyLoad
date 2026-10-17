using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Associates exact authenticated resource lifetimes with the explicitly supplied original work owner.</summary>
internal interface IResourceLifetimeWorkBinding
{
    ResourceLifetimeWorkResultRetention ResultRetention
    {
        get;
    }

    void Bind(ResourceLifetimeCoverageV1 coverage);

    void Bind(ImmutableArray<ResourceLifetimeV1> actualLifetimes);
}
