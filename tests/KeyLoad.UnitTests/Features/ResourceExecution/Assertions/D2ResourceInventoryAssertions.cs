using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class D2ResourceInventoryAssertions
{
    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(NativeSerialization.Serialize(expected).SequenceEqual(NativeSerialization.Serialize(actual))).IsTrue();

    internal static async Task EqualAsync(ResourceOwnerInventoryEntryV1 expected, ResourceOwnerInventoryEntryV1 actual)
    {
        // Entry values are independent of Orleans cross-field object-reference sharing.
        await Assert.That(actual.Lifetime).IsEqualTo(expected.Lifetime);
        await Assert.That(actual.Partition).IsEqualTo(expected.Partition);
        await OwnerAsync(expected.ObservedOwner, actual.ObservedOwner);
        await Assert.That(actual.CurrentPlacement with
        {
            VoterIds = expected.CurrentPlacement.VoterIds
        })
            .IsEqualTo(expected.CurrentPlacement);
        await Assert.That(actual.CurrentPlacement.VoterIds.SequenceEqual(expected.CurrentPlacement.VoterIds,
            StringComparer.Ordinal)).IsTrue();
        await OwnerAsync(expected.CurrentRoute.Owner, actual.CurrentRoute.Owner);
        await Assert.That(actual.CurrentRoute.Endpoints.SequenceEqual(expected.CurrentRoute.Endpoints,
            StringComparer.Ordinal)).IsTrue();
    }

    private static async Task OwnerAsync(PhysicalShardRecord expected, PhysicalShardRecord actual)
    {
        await Assert.That(actual with
        {
            VoterIds = expected.VoterIds
        }).IsEqualTo(expected);
        await Assert.That(actual.VoterIds.SequenceEqual(expected.VoterIds, StringComparer.Ordinal)).IsTrue();
    }

    internal static async Task RowsAsync(D2ResourceInventoryFixture fixture, D2ResourceInventorySeed seed,
        ResourceLifetimeV1 lifetime, CancellationToken token, PartitionRef[]? expectedPartitions = null)
    {
        var partitions = expectedPartitions ?? D2ResourceInventoryData.Partitions;
        var before = ControlledPartitionMovementExpiryOwnerState.Capture(fixture.Owner);
        var work = fixture.Work(token);
        var control = fixture.Capture(lifetime, work);
        await EqualAsync(lifetime, control.Lifetime);
        await Assert.That(control.Request.ExpectedIncarnation).IsEqualTo(lifetime.Incarnation);
        ResourceOwnerInventoryCursorV1? cursor = null;
        ResourceOwnerInventoryOwnerCutV1? cut = null;
        for (var index = 0; index < partitions.Length; index++)
        {
            var page = fixture.Page(control, seed.Observed, cursor, work);
            await Assert.That(page.Entries.Length).IsEqualTo(1);
            await EqualAsync(Expected(lifetime, partitions[index]), page.Entries.Single());
            if (cut is null)
            {
                cut = page.Cut;
                await CutAsync(fixture, cut, seed.Observed);
            }
            else
            {
                await EqualAsync(cut, page.Cut);
            }
            cursor = page.Continuation;
            if (index < partitions.Length - 1)
            {
                await Assert.That(cursor).IsNotNull();
                await CursorAsync(control, page.Cut, cursor!);
            }
            else
            {
                await Assert.That(cursor).IsNull();
            }
        }
        fixture.Database.ValidateResourceOwnerInventoryOwner(D2ResourceInventoryData.Administrator,
            control, cut!, work);
        fixture.Database.ValidateResourceOwnerInventoryControl(D2ResourceInventoryData.Administrator, control, work);
        await before.AssertUnchangedAsync(fixture.Owner);
    }

    internal static async Task EmptyAsync(D2ResourceInventoryFixture fixture, D2ResourceInventorySeed seed, CancellationToken token)
    {
        var before = ControlledPartitionMovementExpiryOwnerState.Capture(fixture.Owner);
        var work = fixture.Work(token);
        var control = fixture.Capture(seed.C, work);
        var page = fixture.Page(control, seed.Observed, null, work);
        await Assert.That(page.Entries).IsEmpty();
        await Assert.That(page.Continuation).IsNull();
        await CutAsync(fixture, page.Cut, seed.Observed);
        fixture.Database.ValidateResourceOwnerInventoryOwner(D2ResourceInventoryData.Administrator, control, page.Cut, work);
        fixture.Database.ValidateResourceOwnerInventoryControl(D2ResourceInventoryData.Administrator, control, work);
        await before.AssertUnchangedAsync(fixture.Owner);
    }

    internal static ResourceOwnerInventoryEntryV1 Expected(ResourceLifetimeV1 lifetime, PartitionRef partition)
    {
        var owner = D2ResourceInventoryData.Control.Owner;
        var placement = new AtomicPartitionPlacementResolution(D2ResourceInventoryData.Version, partition, owner.PhysicalShardId,
            owner.Incarnation, owner.VoterIds, owner.PlacementEpoch, D2ResourceInventoryData.EmptyRevision,
            D2ResourceInventoryData.EmptyRevision, true);
        return new(lifetime, partition, owner, placement, D2ResourceInventoryData.Control);
    }

    private static async Task CutAsync(D2ResourceInventoryFixture fixture, ResourceOwnerInventoryOwnerCutV1 cut,
        RegisteredPhysicalOwnerV1 observed)
    {
        var identity = fixture.Owner.Store.Identity;
        await Assert.That(cut.WriterVersion).IsGreaterThan(0);
        await Assert.That(cut.NodeId).IsEqualTo(identity.NodeId);
        await Assert.That(cut.StoreIncarnation).IsEqualTo(identity.Incarnation);
        await Assert.That(cut.ReadGeneration).IsEqualTo(identity.ReadGeneration);
        await EqualAsync(observed.Owner, cut.ObservedOwner);
        await Assert.That(cut.EnrollmentRevision).IsGreaterThan(0);
        await Assert.That(cut.CatalogRevision).IsEqualTo(D2ResourceInventoryData.InitialRevision);
        await Assert.That(cut.OwnerDirectoryRevision).IsEqualTo(D2ResourceInventoryData.InitialRevision);
        await Assert.That(cut.PlacementDirectoryRevision).IsEqualTo(D2ResourceInventoryData.EmptyRevision);
        await Assert.That(cut.StorePosition).IsEqualTo(fixture.Owner.Store.Position);
        await Assert.That(cut.AppliedIndex).IsEqualTo(fixture.Database.LastApplied);
    }

    private static async Task CursorAsync(ResourceOwnerInventoryControlV1 control,
        ResourceOwnerInventoryOwnerCutV1 cut, ResourceOwnerInventoryCursorV1 cursor)
    {
        await Assert.That(cursor.InventoryId).IsEqualTo(control.Request.InventoryId);
        await Assert.That(cursor.Incarnation).IsEqualTo(control.Lifetime.Incarnation);
        await EqualAsync(cut, cursor.Cut);
        await Assert.That(cursor.PrincipalId).IsEqualTo(control.PrincipalId);
        await Assert.That(cursor.PolicyEpoch).IsEqualTo(control.PolicyEpoch);
        await Assert.That(cursor.ExpiresAt).IsEqualTo(control.Request.ExpiresAt);
        await Assert.That(cursor.AfterKey.IsEmpty).IsFalse();
        var independent = cursor.AfterKey.ToArray();
        independent[0] ^= byte.MaxValue;
        await Assert.That(cursor.AfterKey.Span.SequenceEqual(independent)).IsFalse();
    }
}
