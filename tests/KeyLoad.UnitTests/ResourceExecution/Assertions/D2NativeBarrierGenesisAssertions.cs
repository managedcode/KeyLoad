using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class D2NativeBarrierGenesisAssertions
{
    internal static async Task InventoryAsync(D2ResourceInventoryFixture fixture, ResourceLifetimeV1 lifetime,
        bool proven, CancellationToken token)
    {
        if (!proven)
        {
            await D2ResourceInventoryUnknownAssertions.RefusedAsync(fixture, token);
            return;
        }
        var before = ControlledPartitionMovementExpiryOwnerState.Capture(fixture.Owner);
        var work = fixture.Work(token);
        var control = fixture.Capture(lifetime, work);
        var page = fixture.Page(control, D2ResourceInventoryData.Control, null, work);
        var entry = await Assert.That(page.Entries).HasSingleItem();
        await D2ResourceInventoryAssertions.EqualAsync(lifetime, entry.Lifetime);
        await D2ResourceInventoryAssertions.EqualAsync(D2ResourceInventoryData.Partitions[D2ResourceInventoryData.EmptyRevision], entry.Partition);
        await D2ResourceInventoryAssertions.EqualAsync(D2ResourceInventoryData.Control.Owner, entry.ObservedOwner);
        await D2ResourceInventoryAssertions.EqualAsync(D2ResourceInventoryData.Control, entry.CurrentRoute);
        await Assert.That(entry.CurrentPlacement.PhysicalShardId).IsEqualTo(D2ResourceInventoryData.Control.Owner.PhysicalShardId);
        await Assert.That(entry.CurrentPlacement.Incarnation).IsEqualTo(D2ResourceInventoryData.Control.Owner.Incarnation);
        await Assert.That(page.Cut.EnrollmentRevision).IsGreaterThan(D2ResourceInventoryData.EmptyRevision);
        await Assert.That(page.Cut.AppliedIndex).IsEqualTo(fixture.Database.LastApplied);
        await Assert.That(page.Continuation).IsNull();
        await before.AssertUnchangedAsync(fixture.Owner);
    }

    internal static async Task ReplayAsync(D2ResourceInventoryFixture fixture,
        IReadOnlyList<(ReplicatedOperation Operation, OperationResult Outcome)> writes, CancellationToken token)
    {
        var before = ControlledPartitionMovementExpiryOwnerState.Capture(fixture.Owner);
        var ids = new[] { D2ResourceInventoryData.DocumentId, D2ResourceInventoryUnknownSeed.FollowupId };
        await Assert.That(writes.Count).IsEqualTo(ids.Length);
        for (var index = D2ResourceInventoryData.EmptyRevision; index < writes.Count; index++)
        {
            var repeated = fixture.Submit(writes[index].Operation, token);
            await D2ResourceInventoryAssertions.EqualAsync(writes[index].Outcome, repeated);
            var actual = fixture.Database.GetDocument(D2ResourceInventoryData.Root,
                new(D2ResourceInventoryData.Partitions[D2ResourceInventoryData.EmptyRevision], D2ResourceInventoryData.A, ids[index]),
                cancellationToken: token);
            await Assert.That(actual).IsNotNull();
            await Assert.That(actual!.Json).IsEqualTo(D2ResourceInventoryData.Json[index]);
            await Assert.That(actual.Revision).IsEqualTo(D2ResourceInventoryData.InitialRevision);
        }
        await before.AssertUnchangedAsync(fixture.Owner);
    }
}
