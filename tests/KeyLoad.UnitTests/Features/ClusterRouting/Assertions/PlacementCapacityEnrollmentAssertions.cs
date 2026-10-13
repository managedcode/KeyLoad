using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Serialization;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class PlacementCapacityEnrollmentAssertions
{
    private const int DirectoryReads = 1;

    internal static async Task HealthyAsync(PhysicalShardCatalogFixture fixture,
        ImmutableArray<PhysicalShardRecord> owners)
    {
        var expected = PhysicalOwnerDirectorySerialization.Key().Length
            + PhysicalOwnerDirectoryWholeFlow.DirectoryBytes(fixture).LongLength;
        var work = PlacementCapacityEnrollmentFlow.Admit(fixture, owners);
        await Assert.That(work.ReadBytes).IsEqualTo(expected);
        await Assert.That(work.ConstrainedNativeRecords).IsEqualTo(DirectoryReads);
    }

    internal static async Task RefusedAsync(PhysicalShardCatalogFixture fixture,
        ImmutableArray<PhysicalShardRecord> owners)
    {
        var image = PhysicalOwnerDirectoryWholeFlow.Image(fixture);
        var position = fixture.Store.Position;
        var error = Assert.ThrowsExactly<KeyLoadException>(() =>
            PlacementCapacityEnrollmentFlow.Admit(fixture, owners));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(error.Message).IsEqualTo("The original complete owner cohort changed.");
        await UnchangedAsync(fixture, image, position);
    }

    internal static async Task UnchangedAsync(PhysicalShardCatalogFixture fixture,
        string[] image, long position)
    {
        await Assert.That(fixture.Store.Position).IsEqualTo(position);
        await Assert.That(PhysicalOwnerDirectoryWholeFlow.Image(fixture).SequenceEqual(image)).IsTrue();
    }
}
