using System.Collections.Immutable;
using KeyLoad.Clustering;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Server;

/// <summary>Copies only the lifetimes actually observed by this admitted local storage read.</summary>
internal sealed class BackgroundStorageReadBinding(NativeCapabilityWorkLifetime original) : IResourceLifetimeWorkBinding
{
    private readonly HashSet<ResourceLifetimeV1> observed = [];
    public ResourceLifetimeWorkResultRetention ResultRetention => original.ResultRetention;

    public void Bind(ResourceLifetimeCoverageV1 coverage) => Bind(coverage.Lifetimes);

    public void Bind(ImmutableArray<ResourceLifetimeV1> lifetimes)
    {
        original.Bind(lifetimes);
        observed.UnionWith(lifetimes);
    }

    internal ReadOnlyMemory<byte> Capture(int maximumBytes)
    {
        var lifetimes = observed.ToImmutableArray();
        if (NativeSerialization.Measure(lifetimes) > maximumBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, GrainRoutingProtocol.ReplyBudgetExceeded);
        }
        return NativeSerialization.Serialize(lifetimes);
    }
}
