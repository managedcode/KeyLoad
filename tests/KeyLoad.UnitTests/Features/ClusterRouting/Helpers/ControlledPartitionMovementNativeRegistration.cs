using KeyLoad.CrashHost.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Requires the exact original startup-owned directory without adding registration writes.</summary>
internal static class ControlledPartitionMovementNativeRegistration
{
    private const int Version = 1;
    private const long RegisteredRevision = 1;

    internal static async Task VerifyAsync(ControlledPartitionMovementNativeHosts owners)
    {
        var actual = owners.Source.Database.ReadPhysicalOwnerDirectory(PhysicalShardCatalogFixture.RootPrincipalId);
        var expected = new PhysicalOwnerDirectoryV1(Version, RegisteredRevision, owners.Control.Owner,
            [owners.Control, owners.Destination]);
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }
}
