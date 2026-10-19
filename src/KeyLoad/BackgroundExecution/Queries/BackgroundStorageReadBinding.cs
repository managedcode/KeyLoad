using System.Collections.Immutable;
using KeyLoad.Clustering;
using KeyLoad.Core.Features.CommandOutcomes.Contracts;
using KeyLoad.Core.Features.RuntimeJournaling.Contracts;
using KeyLoad.Core.Features.StoragePartitions.Contracts;
using KeyLoad.Core.Features.ResourceExecution;

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

    internal ReadOnlyMemory<byte> Capture(int maximumBytes)
    {
        var lifetimes = observed.ToImmutableArray();
        if (OrleansSerialization.Measure(lifetimes) > maximumBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, GrainRoutingProtocol.ReplyBudgetExceeded);
        }
        return OrleansSerialization.Serialize(lifetimes);
    }
}
