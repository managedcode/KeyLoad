using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.UnitTests.Features.QueryExecution;

/// <summary>Checks genuine progress on every observed execution physical owner in the original cluster.</summary>
internal static class QueryHostedReadAssertions
{
    internal static async Task ProgressAsync(RequestCqrsHostedCluster cluster, QueryHostedReadObservation read)
    {
        await Assert.That(read.PhysicalReads.Length).IsGreaterThan(0);
        await OwnerAsync(cluster, read.Coordinator).ConfigureAwait(false);
        foreach (var owner in read.LeafOwners)
        {
            await OwnerAsync(cluster, owner).ConfigureAwait(false);
        }
        foreach (var physical in read.PhysicalReads)
        {
            await OwnerAsync(cluster, physical.Owner).ConfigureAwait(false);
            await Assert.That(physical.ObservedBytes).IsGreaterThan(physical.BeforeBytes);
        }
    }

    private static async Task OwnerAsync(RequestCqrsHostedCluster cluster, QueryHostedStorageOwner owner)
    {
        var node = cluster.Nodes.Single(node => ReferenceEquals(node.Database.Store, owner.Store));
        await Assert.That(owner.NodeId).IsEqualTo(node.Database.Store.Identity.NodeId);
        await Assert.That(owner.Silo).IsEqualTo(node.Services.GetRequiredService<ILocalSiloDetails>().SiloAddress);
        await Assert.That(ReferenceEquals(owner.Clock, node.Clock)).IsTrue();
    }
}
