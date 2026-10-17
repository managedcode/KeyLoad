using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class ResourcePartitionInventoryModelsInventoryAssertions
{
    internal static async Task<ResourceOwnerInventoryResultV1> ReadAsync(TwoRf3MembershipWave wave,
        ResourcePartitionInventoryRf3Seed seed, RemoteTransferNativeMembership membership,
        ResourceDefinition definition, bool populated, CancellationToken token)
    {
        var result = await ResourcePartitionInventoryRf3Assertions.SuccessAsync(
            await ResourcePartitionInventoryRf3Bridge.ReadAsync(wave, seed, membership, definition.Name, token));
        await SqlRf3Protocol.EqualAsync(definition, result.Control.Definition);
        var lifetime = result.Control.Lifetime;
        await Assert.That(lifetime.TenantId).IsEqualTo(ResourcePartitionInventoryModelsProtocol.Partition.TenantId);
        await Assert.That(lifetime.DatabaseId).IsEqualTo(ResourcePartitionInventoryModelsProtocol.Partition.DatabaseId);
        await Assert.That(lifetime.ResourceName).IsEqualTo(definition.Name);
        await Assert.That(lifetime.TransactionDomainId).IsEqualTo(definition.TransactionDomainId);
        await Assert.That(lifetime.Kind).IsEqualTo(definition.Kind);
        await Assert.That(result.Control.PrincipalId).IsEqualTo(PartitionMovementPublicParentRf3Administrator.PrincipalId);
        if (!populated)
        {
            await Assert.That(result.Entries).IsEmpty();
            return result;
        }
        var entry = await Assert.That(result.Entries).HasSingleItem();
        await SqlRf3Protocol.EqualAsync(lifetime, entry.Lifetime);
        await SqlRf3Protocol.EqualAsync(ResourcePartitionInventoryModelsProtocol.Partition, entry.Partition);
        var directory = await PhysicalOwnerDirectoryRf3Assertions.ExpectedAsync(wave.Application, wave.Profile, token);
        await SqlRf3Protocol.EqualAsync(directory, result.Control.Directory);
        await SqlRf3Protocol.EqualAsync(directory.ControlOwner, entry.ObservedOwner);
        await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1,
            seed.Credential, async callers =>
            {
                var placement = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadAtomicPartitionPlacementAsync(
                    new(ResourcePartitionInventoryModelsProtocol.Version, ResourcePartitionInventoryModelsProtocol.Partition), token));
                await SqlRf3Protocol.EqualAsync(placement, entry.CurrentPlacement);
                await SqlRf3Protocol.EqualAsync(directory.Owners.Single(owner =>
                    owner.Owner.PhysicalShardId == placement.PhysicalShardId), entry.CurrentRoute);
                return true;
            }, token);
        return result;
    }

    internal static async Task FirstEffectAsync(ResourceOwnerInventoryResultV1 before,
        ResourceOwnerInventoryResultV1 after)
    {
        await SqlRf3Protocol.EqualAsync(before.Control.Lifetime, after.Control.Lifetime);
        await Assert.That(before.Entries).IsEmpty();
        await Assert.That(after.Entries.Length).IsEqualTo(ResourcePartitionInventoryModelsProtocol.PerFamily);
        foreach (var cut in after.OwnerCuts)
        {
            var prior = before.OwnerCuts.Single(value => value.NodeId == cut.NodeId);
            await Assert.That(cut.StoreIncarnation).IsEqualTo(prior.StoreIncarnation);
            if (cut.ObservedOwner.PhysicalShardId == after.Control.Directory.ControlOwner.PhysicalShardId)
            {
                await Assert.That(cut.EnrollmentRevision).IsGreaterThan(prior.EnrollmentRevision);
            }
            else
            {
                await Assert.That(cut.EnrollmentRevision).IsEqualTo(prior.EnrollmentRevision);
            }
        }
    }

    internal static async Task<ImmutableArray<ResourcePartitionInventoryModelsInventory>> CaptureAsync(
        TwoRf3MembershipWave wave, ResourcePartitionInventoryRf3Seed seed,
        RemoteTransferNativeMembership membership, IEnumerable<ResourceDefinition> definitions, CancellationToken token)
    {
        var inventory = ImmutableArray.CreateBuilder<ResourcePartitionInventoryModelsInventory>();
        foreach (var definition in definitions)
        {
            inventory.Add(new(definition, await ReadAsync(wave, seed, membership, definition, populated: true, token)));
        }
        return inventory.ToImmutable();
    }

    internal static async Task SameAsync(ImmutableArray<ResourcePartitionInventoryModelsInventory> expected,
        ImmutableArray<ResourcePartitionInventoryModelsInventory> actual)
    {
        await Assert.That(actual.Length).IsEqualTo(expected.Length);
        foreach (var original in expected)
        {
            var current = actual.Single(value => value.Definition.Name == original.Definition.Name);
            await SqlRf3Protocol.EqualAsync(original.Definition, current.Definition);
            await SqlRf3Protocol.EqualAsync(original.Result.Control.Lifetime, current.Result.Control.Lifetime);
            await SqlRf3Protocol.EqualAsync(original.Result.Entries, current.Result.Entries);
            await Assert.That(current.Result.OwnerCuts.Select(value => value.NodeId))
                .IsEquivalentTo(original.Result.OwnerCuts.Select(value => value.NodeId));
            foreach (var cut in current.Result.OwnerCuts)
            {
                var prior = original.Result.OwnerCuts.Single(value => value.NodeId == cut.NodeId);
                await Assert.That(cut.StoreIncarnation).IsEqualTo(prior.StoreIncarnation);
                await Assert.That(cut.EnrollmentRevision).IsEqualTo(prior.EnrollmentRevision);
            }
        }
    }
}
