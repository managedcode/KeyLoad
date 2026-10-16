using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class D2ArchiveQuery
{
    internal static async Task VerifyAsync(DatabaseEngine database, IAtomicStore store, D2ArchiveSource source,
        RegisteredPhysicalOwnerV1 currentOwner, CancellationToken token)
    {
        var position = store.Position;
        var image = ControlledPartitionMovementRawImage.Bytes(store);
        foreach (var lifetime in new[] { source.DocumentsLifetime, source.UnrelatedLifetime })
        {
            if (source.Unknown)
            {
                await D2ResourceInventoryRefusals.FailureAsync(() => Read(database, source, lifetime, currentOwner, token),
                    ErrorCode.RecoveryRequired);
                continue;
            }
            var page = Read(database, source, lifetime, currentOwner, token);
            await Assert.That(page.Entries.Length).IsEqualTo(D2ArchiveData.Single);
            await Assert.That(page.Continuation).IsNull();
            var owner = currentOwner.Owner;
            var placement = new AtomicPartitionPlacementResolution(D2ArchiveData.Version, source.Partition,
                owner.PhysicalShardId, owner.Incarnation, owner.VoterIds, owner.PlacementEpoch,
                D2ArchiveData.Empty, D2ArchiveData.Empty, true);
            await D2ResourceInventoryAssertions.EqualAsync(new ResourceOwnerInventoryEntryV1(
                lifetime, source.Partition, owner, placement, currentOwner), page.Entries.Single());
        }
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
    }

    private static ResourceOwnerInventoryPageV1 Read(DatabaseEngine database, D2ArchiveSource source,
        ResourceLifetimeV1 lifetime, RegisteredPhysicalOwnerV1 currentOwner, CancellationToken token)
    {
        var work = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(), database.EvaluationClock, token);
        var request = database.AdmitResourceOwnerInventoryRequest(D2ArchiveData.Root, Guid.NewGuid(),
            source.Partition.TenantId, source.Partition.DatabaseId, lifetime.ResourceName, D2ArchiveData.Single,
            database.EvaluationClock.GetUtcNow().AddSeconds(database.Limits.QueryDeadlineSeconds), work);
        var control = database.CaptureResourceOwnerInventoryControl(D2ArchiveData.Root, request, work);
        var page = database.ReadResourceOwnerInventoryPage(D2ArchiveData.Root, control, currentOwner, null, work);
        database.ValidateResourceOwnerInventoryOwner(D2ArchiveData.Root, control, page.Cut, work);
        database.ValidateResourceOwnerInventoryControl(D2ArchiveData.Root, control, work);
        return page;
    }
}
