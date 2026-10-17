using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class ResourcePartitionInventoryModelsMovedCut
{
    private const string MissingPlacement = "The actual completed movement placement is absent.";

    internal static async Task<ResourcePartitionInventoryModelsState> CaptureAsync(TwoRf3MembershipWave wave,
        ResourcePartitionInventoryRf3Seed seed, ResourcePartitionInventoryModelsState original,
        ResourcePartitionInventoryRf3MoveState movement, CancellationToken token)
    {
        var published = movement.Terminal.PublishedPlacement ?? throw new InvalidOperationException(MissingPlacement);
        using var membership = await RemoteTransferNativeMembership.ReadAsync(wave, token);
        var definitions = original.Inventory.Select(item => item.Definition).ToArray();
        var actual = await ResourcePartitionInventoryModelsInventoryAssertions.CaptureAsync(
            wave, seed, membership, definitions, token);
        var expected = original.Inventory.Select(item => item with
        {
            Result = item.Result with
            {
                OwnerCuts = item.Result.OwnerCuts.Select(cut => cut with
                {
                    EnrollmentRevision = movement.After.First.OwnerCuts.Single(value => value.NodeId == cut.NodeId).EnrollmentRevision
                }).ToImmutableArray(),
                Entries = item.Result.Entries.Select(entry => entry with
                {
                    CurrentPlacement = entry.CurrentPlacement with
                    {
                        DirectoryRevision = published.Revision
                    }
                }).ToImmutableArray()
            }
        }).ToImmutableArray();
        await ResourcePartitionInventoryModelsInventoryAssertions.SameAsync(expected, actual);
        foreach (var current in actual)
        {
            await Assert.That(current.Result.Control.PlacementDirectoryRevision).IsEqualTo(published.Revision);
            await Assert.That(current.Result.Control.PolicyEpoch).IsEqualTo(ResourcePartitionInventoryRf3Warm.RestoredEpoch);
        }
        return original with
        {
            Inventory = actual
        };
    }
}
