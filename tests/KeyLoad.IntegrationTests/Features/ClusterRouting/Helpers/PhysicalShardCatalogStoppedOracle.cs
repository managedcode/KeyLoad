using KeyLoad.Core;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Replication;
using KeyLoad.Security;
using KeyLoad.Server;
using Microsoft.Extensions.Logging.Abstractions;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PhysicalShardCatalogStoppedOracle
{
    private const long InitialCatalogRevision = 1;
    private const long InitialPlacementEpoch = 1;
    private const int CurrentCatalogVersion = 1;

    internal static async Task VerifyAsync(string dataRoot, NodeEpochRf3Profile profile,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < RequestCqrsRf3Protocol.NodeCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = RequestCqrsRf3Protocol.NodeName(index);
            var nodeRoot = Path.Combine(dataRoot, node);
            var options = NodeEpochRf3OfflineOptions.Create(nodeRoot, profile, node);
            var configured = options.CreateReplicaConfiguration(Path.GetFullPath(nodeRoot));
            await using var host = new PartitionHost(options, new AuthorizationPolicy(),
                new CommandAdmissionGovernor(options.CommandAdmission), TimeProvider.System,
                NullLogger<ReplicaConsensus>.Instance);
            var catalog = host.Database.ReadPhysicalShardCatalog(PartitionStoreProtocol.AdministratorId);
            await Assert.That(catalog.Version).IsEqualTo(CurrentCatalogVersion);
            await Assert.That(catalog.Revision).IsEqualTo(InitialCatalogRevision);
            await Assert.That(catalog.DefaultShard.PhysicalShardId).IsEqualTo(profile.PhysicalShardId);
            await Assert.That(catalog.DefaultShard.Incarnation).IsEqualTo(profile.Incarnation);
            await Assert.That(catalog.DefaultShard.VoterIds.SequenceEqual(configured.VoterIds, StringComparer.Ordinal))
                .IsTrue();
            await Assert.That(catalog.DefaultShard.PlacementEpoch).IsEqualTo(InitialPlacementEpoch);
        }
    }
}
