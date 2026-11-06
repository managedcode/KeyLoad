using KeyLoad.Clustering;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime.Placement;

namespace KeyLoad.FunctionalTests.StorageReplication;

/// <summary>Places an original partition coordinator beside or away from its real primary for transport qualification.</summary>
internal static class StorageCoordinatorPlacement
{
    internal static async Task ActivateAsync(RequestCqrsHostedCluster cluster, bool remote, CancellationToken token)
    {
        var source = cluster.Nodes[0];
        var incarnation = source.Database.Store.Identity.Incarnation;
        var primary = await source.Grains.GetGrain<IDatabaseCommitGrain>(DatabaseGrainIdentity.For(incarnation))
            .GetWriteOwnerAsync(token);
        var target = cluster.Nodes.Select(node => node.Services.GetRequiredService<ILocalSiloDetails>().SiloAddress)
            .Single(address => remote != address.Equals(primary));
        const string key = IPlacementDirector.PlacementHintKey;
        var present = RequestContext.Keys.Contains(key, StringComparer.Ordinal);
        var previous = RequestContext.Get(key) /* KLD0041: Preserve the exact framework context slot while using the standard placement hint. */;
        try
        {
            RequestContext.Set(key, target) /* KLD0041: Orleans accepts the original typed SiloAddress through its standard object-valued placement-hint slot. */;
            await source.Grains.GetGrain<IPartitionCommitGrain>(PartitionCommitGrainIdentity.For(incarnation, source.Database.Partition))
                .GetReadOwnerAsync(incarnation, source.Database.Partition, primary, token);
        }
        finally
        {
            GrainRequestIdentityScope.Restore(key, present,
                previous /* KLD0041: Restore the unchanged original framework context slot, including present null. */);
        }
    }
}
