namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class PhysicalOwnerDirectoryWholeFlowTests
{
    private const string AdministratorRequired = "Cluster administration is required.";
    private const string CommandConflict = "The command ID was already used with different content.";
    private const string StaleRevision = "The physical owner directory revision is stale.";

    [Test]
    public async Task AcOwnerRegister001NativeRegistrationReplayAndReopenRetainCompleteLiteralDirectory()
    {
        using var fixture = new PhysicalShardCatalogFixture();
        PhysicalOwnerDirectoryWholeFlow.Bootstrap(fixture);
        var originalCatalog = fixture.ReadCatalogBytes();
        var operation = PhysicalOwnerDirectoryWholeFlow.Operation(fixture,
            PhysicalOwnerDirectoryWholeFlow.Request(), PhysicalOwnerDirectoryWholeFlow.RegistrationId);
        var first = fixture.Database.Apply(operation);
        await Assert.That(first.Error).IsNull();
        await LiteralAsync(fixture.Database.ReadPhysicalOwnerDirectory(PhysicalShardCatalogFixture.RootPrincipalId));
        var image = PhysicalOwnerDirectoryWholeFlow.Image(fixture);
        var position = fixture.Store.Position;
        var replay = fixture.Database.Apply(operation);
        await Assert.That(NativeSerialization.Serialize(replay).SequenceEqual(NativeSerialization.Serialize(first))).IsTrue();
        await Assert.That(PhysicalOwnerDirectoryWholeFlow.Image(fixture).SequenceEqual(image)).IsTrue();
        await Assert.That(fixture.Store.Position).IsEqualTo(position);
        var changed = fixture.Database.Apply(PhysicalOwnerDirectoryWholeFlow.Operation(fixture,
            PhysicalOwnerDirectoryWholeFlow.Request(PhysicalOwnerDirectoryWholeFlow.InitialRevision),
            PhysicalOwnerDirectoryWholeFlow.RegistrationId));
        await Assert.That(changed.Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(changed.SafeDetail).IsEqualTo(CommandConflict);
        await Assert.That(PhysicalOwnerDirectoryWholeFlow.Image(fixture).SequenceEqual(image)).IsTrue();
        await Assert.That(fixture.Store.Position).IsEqualTo(position);
        fixture.Reopen();
        await LiteralAsync(fixture.Database.ReadPhysicalOwnerDirectory(PhysicalShardCatalogFixture.RootPrincipalId));
        await Assert.That(PhysicalOwnerDirectoryWholeFlow.Image(fixture).SequenceEqual(image)).IsTrue();
        await Assert.That(fixture.ReadCatalogBytes().SequenceEqual(originalCatalog)).IsTrue();
        var reopenedReplay = fixture.Database.Apply(operation);
        await Assert.That(NativeSerialization.Serialize(reopenedReplay).SequenceEqual(NativeSerialization.Serialize(first))).IsTrue();
        await Assert.That(fixture.Store.Position).IsEqualTo(position);
        var healthyRead = fixture.Database.ReadPhysicalOwnerDirectory(PhysicalShardCatalogFixture.RootPrincipalId);
        await LiteralAsync(healthyRead);
        await Assert.That(PhysicalOwnerDirectoryWholeFlow.Image(fixture).SequenceEqual(image)).IsTrue();
    }

    [Test]
    public async Task AcOwnerRegister001DeniedLoggedFailureReplaysWithoutDirectoryEffectThenHealthyRegistration()
    {
        using var fixture = new PhysicalShardCatalogFixture();
        PhysicalOwnerDirectoryWholeFlow.Bootstrap(fixture);
        fixture.AddPrincipal(new(PhysicalOwnerDirectoryWholeFlow.ReaderId, PhysicalOwnerDirectoryWholeFlow.TenantId, [], []));
        var originalCatalog = fixture.ReadCatalogBytes();
        var deniedOperation = PhysicalOwnerDirectoryWholeFlow.Operation(fixture,
            PhysicalOwnerDirectoryWholeFlow.Request(), PhysicalOwnerDirectoryWholeFlow.DeniedId,
            PhysicalOwnerDirectoryWholeFlow.ReaderId);
        var denied = fixture.Database.Apply(deniedOperation);
        await Assert.That(denied.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(denied.SafeDetail).IsEqualTo(AdministratorRequired);
        var failedImage = PhysicalOwnerDirectoryWholeFlow.Image(fixture);
        var failedPosition = fixture.Store.Position;
        var replay = fixture.Database.Apply(deniedOperation);
        await Assert.That(NativeSerialization.Serialize(replay).SequenceEqual(NativeSerialization.Serialize(denied))).IsTrue();
        await Assert.That(fixture.Store.Position).IsEqualTo(failedPosition);
        await Assert.That(PhysicalOwnerDirectoryWholeFlow.Image(fixture).SequenceEqual(failedImage)).IsTrue();
        var healthy = fixture.Database.Apply(PhysicalOwnerDirectoryWholeFlow.Operation(fixture,
            PhysicalOwnerDirectoryWholeFlow.Request(), PhysicalOwnerDirectoryWholeFlow.RegistrationId));
        await Assert.That(healthy.Error).IsNull();
        await LiteralAsync(fixture.Database.ReadPhysicalOwnerDirectory(PhysicalShardCatalogFixture.RootPrincipalId));
        var registeredBytes = PhysicalOwnerDirectoryWholeFlow.DirectoryBytes(fixture);
        var stale = fixture.Database.Apply(PhysicalOwnerDirectoryWholeFlow.Operation(fixture,
            PhysicalOwnerDirectoryWholeFlow.Request(), Guid.NewGuid()));
        await Assert.That(stale.Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(stale.SafeDetail).IsEqualTo(StaleRevision);
        await Assert.That(PhysicalOwnerDirectoryWholeFlow.DirectoryBytes(fixture).SequenceEqual(registeredBytes)).IsTrue();
        await Assert.That(fixture.ReadCatalogBytes().SequenceEqual(originalCatalog)).IsTrue();
        await LiteralAsync(fixture.Database.ReadPhysicalOwnerDirectory(PhysicalShardCatalogFixture.RootPrincipalId));
    }

    private static async Task LiteralAsync(PhysicalOwnerDirectoryV1 actual)
    {
        await Assert.That(NativeSerialization.Serialize(actual).SequenceEqual(
            NativeSerialization.Serialize(PhysicalOwnerDirectoryWholeFlow.Expected()))).IsTrue();
    }
}
