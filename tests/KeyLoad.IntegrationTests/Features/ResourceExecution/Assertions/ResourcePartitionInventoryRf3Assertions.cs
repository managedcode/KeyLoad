using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class ResourcePartitionInventoryRf3Assertions
{
    private const int WriterVersion = 1;

    internal static async Task<ResourceOwnerInventoryResultV1> SuccessAsync(ResourcePartitionInventoryRf3Reply reply)
    {
        await Assert.That(reply.Error).IsNull();
        await Assert.That(reply.Witness).IsNotNull();
        var result = reply.Witness!.Inventory;
        await Assert.That(result.Complete).IsTrue();
        await Assert.That(result.CoverageKind).IsEqualTo(ResourceOwnerInventoryCoverageKind.ConservativeMayContain);
        var expectedCuts = result.Control.Directory.Owners.Sum(owner => owner.Owner.VoterIds.Length);
        await Assert.That(result.OwnerCuts.Length).IsEqualTo(expectedCuts);
        await Assert.That(result.OwnerCuts.Select(cut => cut.NodeId).Distinct().Count()).IsEqualTo(expectedCuts);
        foreach (var cut in result.OwnerCuts)
        {
            await Assert.That(cut.NodeId).IsNotEqualTo(Guid.Empty);
            await Assert.That(cut.StoreIncarnation).IsNotEqualTo(Guid.Empty);
            await Assert.That(cut.ReadGeneration).IsGreaterThanOrEqualTo(0L);
            await Assert.That(cut.WriterVersion).IsEqualTo(WriterVersion);
            await Assert.That(cut.AppliedIndex).IsGreaterThanOrEqualTo(0L);
            await Assert.That(cut.StorePosition).IsGreaterThanOrEqualTo(0L);
        }
        return result;
    }

    internal static async Task RefusalAsync(ResourcePartitionInventoryRf3Reply reply, ErrorCode expected)
    {
        await Assert.That(reply.Witness).IsNull();
        await Assert.That(reply.Error).IsEqualTo(expected);
    }

    internal static async Task<ResourcePartitionInventoryRf3Baseline> CaptureAsync(TwoRf3MembershipWave wave,
        ResourcePartitionInventoryRf3Seed seed, RemoteTransferNativeMembership membership, long epoch,
        CancellationToken token)
    {
        var first = await ReadAsync(wave, seed, membership, seed.Creations[0].Request.Definition, ResourcePartitionInventoryRf3Seed.Partitions, epoch, token);
        var second = await ReadAsync(wave, seed, membership, seed.Creations[1].Request.Definition, ResourcePartitionInventoryRf3Seed.Partitions, epoch, token);
        var unused = await ReadAsync(wave, seed, membership, seed.Creations[2].Request.Definition, [], epoch, token);
        return new(first, second, unused);
    }

    internal static async Task SameMembershipAsync(ResourcePartitionInventoryRf3Baseline expected,
        ResourcePartitionInventoryRf3Baseline actual)
    {
        await SameAsync(expected.First, actual.First);
        await SameAsync(expected.Second, actual.Second);
        await SameAsync(expected.Unused, actual.Unused);
    }

    private static async Task SameAsync(ResourceOwnerInventoryResultV1 expected, ResourceOwnerInventoryResultV1 actual)
    {
        await SqlRf3Protocol.EqualAsync(expected.Control.Lifetime, actual.Control.Lifetime);
        await SqlRf3Protocol.EqualAsync(expected.Control.Definition, actual.Control.Definition);
        await SqlRf3Protocol.EqualAsync(expected.Entries, actual.Entries);
        await Assert.That(actual.OwnerCuts.Select(cut => cut.NodeId).Order())
            .IsEquivalentTo(expected.OwnerCuts.Select(cut => cut.NodeId).Order());
        foreach (var current in actual.OwnerCuts)
        {
            var prior = expected.OwnerCuts.Single(cut => cut.NodeId == current.NodeId);
            await Assert.That(current.StoreIncarnation).IsEqualTo(prior.StoreIncarnation);
            await Assert.That(current.EnrollmentRevision).IsEqualTo(prior.EnrollmentRevision);
        }
    }

    private static async Task<ResourceOwnerInventoryResultV1> ReadAsync(TwoRf3MembershipWave wave,
        ResourcePartitionInventoryRf3Seed seed, RemoteTransferNativeMembership membership, ResourceDefinition definition,
        System.Collections.Immutable.ImmutableArray<PartitionRef> partitions, long epoch, CancellationToken token)
    {
        var result = await SuccessAsync(await ResourcePartitionInventoryRf3Bridge.ReadAsync(
            wave, seed, membership, definition.Name, token));
        var directory = await PhysicalOwnerDirectoryRf3Assertions.ExpectedAsync(wave.Application, wave.Profile, token);
        await SqlRf3Protocol.EqualAsync(directory, result.Control.Directory);
        await SqlRf3Protocol.EqualAsync(definition, result.Control.Definition);
        await Assert.That(result.Control.PrincipalId).IsEqualTo(PartitionMovementPublicParentRf3Administrator.PrincipalId);
        await Assert.That(result.Control.PolicyEpoch).IsEqualTo(epoch);
        await LifetimeAsync(result.Control.Lifetime, definition);
        await Assert.That(result.Entries.Length).IsEqualTo(partitions.Length);
        foreach (var owner in directory.Owners)
        {
            var cuts = result.OwnerCuts.Where(cut => cut.ObservedOwner.PhysicalShardId == owner.Owner.PhysicalShardId).ToArray();
            await Assert.That(cuts.Length).IsEqualTo(owner.Owner.VoterIds.Length);
            foreach (var cut in cuts)
            {
                await SqlRf3Protocol.EqualAsync(owner.Owner, cut.ObservedOwner);
                await Assert.That(cut.OwnerDirectoryRevision).IsEqualTo(
                    owner.Owner.PhysicalShardId == directory.ControlOwner.PhysicalShardId ? directory.Revision : 0L);
            }
        }
        await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1, seed.Credential,
            async callers =>
            {
                foreach (var partition in partitions)
                {
                    var entry = result.Entries.Single(value => value.Partition == partition);
                    await SqlRf3Protocol.EqualAsync(result.Control.Lifetime, entry.Lifetime);
                    await SqlRf3Protocol.EqualAsync(directory.ControlOwner, entry.ObservedOwner);
                    var placement = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk
                        .ReadAtomicPartitionPlacementAsync(new(1, partition), token));
                    await SqlRf3Protocol.EqualAsync(placement, entry.CurrentPlacement);
                    await SqlRf3Protocol.EqualAsync(directory.Owners.Single(owner =>
                        owner.Owner.PhysicalShardId == placement.PhysicalShardId), entry.CurrentRoute);
                }
                return true;
            }, token);
        return result;
    }

    private static async Task LifetimeAsync(ResourceLifetimeV1 lifetime, ResourceDefinition definition)
    {
        await Assert.That(lifetime.TenantId).IsEqualTo(ResourcePartitionInventoryRf3Protocol.Tenant);
        await Assert.That(lifetime.DatabaseId).IsEqualTo(ResourcePartitionInventoryRf3Protocol.Database);
        await Assert.That(lifetime.ResourceName).IsEqualTo(definition.Name);
        await Assert.That(lifetime.TransactionDomainId).IsEqualTo(definition.TransactionDomainId);
        await Assert.That(lifetime.Kind).IsEqualTo(ResourceKind.Collection);
        await Assert.That(lifetime.CreationFingerprint.Length).IsEqualTo(64);
        await Assert.That(lifetime.Incarnation.Value.Length).IsEqualTo(64);
    }
}
