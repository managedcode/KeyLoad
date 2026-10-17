using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class ResourcePartitionInventoryRf3MoveInventory
{
    private const int PlacementVersion = 1;
    private const int ImportedPartition = 1;
    private const long ImportedLifetimes = 2L;
    private const long VerifiedImportRevision = 1L;
    private const long MissingDirectoryRevision = 0L;
    private const string MissingPlacement = "The original completed movement placement is absent.";

    internal static async Task<ResourcePartitionInventoryRf3Baseline> CaptureAsync(TwoRf3MembershipWave wave,
        ResourcePartitionInventoryRf3Seed seed, RemoteTransferNativeMembership membership,
        ResourcePartitionInventoryRf3Baseline original, PartitionMoveResult terminal, CancellationToken token)
        => new(await ReadAsync(wave, seed, membership, original.First, terminal, token),
            await ReadAsync(wave, seed, membership, original.Second, terminal, token),
            await ReadAsync(wave, seed, membership, original.Unused, terminal, token));

    private static Task<ResourceOwnerInventoryResultV1> ReadAsync(TwoRf3MembershipWave wave,
        ResourcePartitionInventoryRf3Seed seed, RemoteTransferNativeMembership membership,
        ResourceOwnerInventoryResultV1 original, PartitionMoveResult terminal, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1, seed.Credential,
            async callers =>
            {
                var actual = await ResourcePartitionInventoryRf3Assertions.SuccessAsync(
                    await ResourcePartitionInventoryRf3Bridge.ReadAsync(wave, seed, membership,
                        original.Control.Definition.Name, token));
                await SqlRf3Protocol.EqualAsync(original.Control.Definition, actual.Control.Definition);
                await SqlRf3Protocol.EqualAsync(original.Control.Lifetime, actual.Control.Lifetime);
                await SqlRf3Protocol.EqualAsync(original.Control.Catalog, actual.Control.Catalog);
                await Assert.That(actual.Control.PolicyEpoch).IsEqualTo(ResourcePartitionInventoryRf3Warm.RestoredEpoch);
                await Assert.That(actual.Control.PrincipalId).IsEqualTo(original.Control.PrincipalId);
                var published = terminal.PublishedPlacement ?? throw new InvalidOperationException(MissingPlacement);
                await Assert.That(actual.Control.PlacementDirectoryRevision).IsEqualTo(published.Revision);
                var candidates = original.Entries.Select(entry => (entry.Partition, entry.ObservedOwner)).ToList();
                if (!original.Entries.IsEmpty)
                {
                    candidates.Add((terminal.Partition, terminal.DestinationOwner));
                    await Assert.That(actual.Entries.Length).IsEqualTo(original.Entries.Length + ImportedPartition);
                }
                else
                {
                    await Assert.That(actual.Entries).IsEmpty();
                }
                foreach (var (partition, owner) in candidates)
                {
                    var placement = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk
                        .ReadAtomicPartitionPlacementAsync(new(PlacementVersion, partition), token));
                    var expected = partition == terminal.Partition
                        ? new AtomicPartitionPlacementResolution(PlacementVersion, partition,
                            published.PhysicalShardId, published.Incarnation, published.VoterIds,
                            published.PlacementEpoch, published.Revision, published.Revision, false)
                        : original.Entries.Single(entry => entry.Partition == partition).CurrentPlacement with
                        {
                            DirectoryRevision = published.Revision
                        };
                    await SqlRf3Protocol.EqualAsync(expected, placement);
                    var route = actual.Control.Directory.Owners.Single(entry =>
                        entry.Owner.PhysicalShardId == placement.PhysicalShardId);
                    var entry = actual.Entries.Single(entry => entry.Partition == partition
                        && entry.ObservedOwner.PhysicalShardId == owner.PhysicalShardId);
                    await SqlRf3Protocol.EqualAsync(new ResourceOwnerInventoryEntryV1(
                        original.Control.Lifetime, partition, owner, placement, route), entry);
                }
                await OwnersAsync(original, actual, terminal);
                return actual;
            }, token);

    private static async Task OwnersAsync(ResourceOwnerInventoryResultV1 original,
        ResourceOwnerInventoryResultV1 actual, PartitionMoveResult terminal)
    {
        foreach (var owner in actual.Control.Directory.Owners)
        {
            var cuts = actual.OwnerCuts.Where(cut => cut.ObservedOwner.PhysicalShardId == owner.Owner.PhysicalShardId).ToArray();
            await Assert.That(cuts.Length).IsEqualTo(owner.Owner.VoterIds.Length);
            foreach (var cut in cuts)
            {
                await SqlRf3Protocol.EqualAsync(owner.Owner, cut.ObservedOwner);
                var prior = original.OwnerCuts.Single(value => value.NodeId == cut.NodeId);
                var expectedRevision = owner.Owner.PhysicalShardId == terminal.DestinationOwner.PhysicalShardId
                    ? checked(prior.EnrollmentRevision + ImportedLifetimes + VerifiedImportRevision) : prior.EnrollmentRevision;
                await Assert.That(cut.EnrollmentRevision).IsEqualTo(expectedRevision);
                await Assert.That(cut.StoreIncarnation).IsEqualTo(prior.StoreIncarnation);
                await Assert.That(cut.CatalogRevision).IsEqualTo(prior.CatalogRevision);
                await Assert.That(cut.OwnerDirectoryRevision).IsEqualTo(owner.Owner.PhysicalShardId
                    == actual.Control.Directory.ControlOwner.PhysicalShardId
                    ? actual.Control.Directory.Revision : MissingDirectoryRevision);
            }
        }
    }
}
