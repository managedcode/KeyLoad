namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class PhysicalOwnerDirectoryReadAuthorityAssertions
{
    private const string AdministratorRequired = "Cluster administration is required.";
    private const string ControlMismatch = "The physical owner directory does not match the control authority.";

    internal static async Task DeniedAsync(PhysicalShardCatalogFixture fixture)
    {
        var denied = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Database.ReadPhysicalOwnerDirectory(
            PhysicalOwnerDirectoryReadAuthorityFlow.Administrator));
        await Assert.That(denied.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(denied.Message).IsEqualTo(AdministratorRequired);
    }

    internal static async Task DeniedAndCorruptAsync(PhysicalShardCatalogFixture fixture)
    {
        await DeniedAsync(fixture);
        var corruption = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Database.ReadPhysicalOwnerDirectory(
            PhysicalShardCatalogFixture.RootPrincipalId));
        await Assert.That(corruption.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(corruption.Message).IsEqualTo(ControlMismatch);
    }

    internal static async Task LiteralAsync(PhysicalShardCatalogFixture fixture, string principal)
    {
        var actual = fixture.Database.ReadPhysicalOwnerDirectory(principal);
        await Assert.That(NativeSerialization.Serialize(actual).SequenceEqual(
            NativeSerialization.Serialize(PhysicalOwnerDirectoryWholeFlow.Expected()))).IsTrue();
    }

    internal static async Task UnchangedAsync(PhysicalShardCatalogFixture fixture, byte[] image, long position)
    {
        await Assert.That(PhysicalShardCatalogPolicyOutcomeAssertions.Image(fixture).SequenceEqual(image)).IsTrue();
        await Assert.That(fixture.Store.Position).IsEqualTo(position);
    }
}
