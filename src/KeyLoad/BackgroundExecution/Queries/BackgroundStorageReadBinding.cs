using System.Collections.Immutable;
using KeyLoad.Clustering;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Core.StoragePartitions.Contracts;

namespace KeyLoad.Server;

/// <summary>Copies only the lifetimes actually observed by this admitted local storage read.</summary>
internal sealed class BackgroundStorageReadBinding(CapabilityWorkLifetime original) : IResourceLifetimeWorkBinding
{
    private readonly HashSet<ResourceLifetime> observed = [];
    public ResourceLifetimeWorkResultRetention ResultRetention => original.ResultRetention;

    public void Bind(ResourceLifetimeCoverage coverage) => Bind(coverage.Lifetimes);

    public void Bind(ImmutableArray<ResourceLifetime> lifetimes)
    {
        original.Bind(lifetimes);
        observed.UnionWith(lifetimes);
    }

    internal ImmutableArray<ResourceLifetime> Capture() => [.. observed];
}
