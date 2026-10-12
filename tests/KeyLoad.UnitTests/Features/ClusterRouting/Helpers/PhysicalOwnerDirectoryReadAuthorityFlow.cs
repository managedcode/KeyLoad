using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class PhysicalOwnerDirectoryReadAuthorityFlow
{
    internal const string Administrator = "directory-read-administrator";
    private const long GrantedEpoch = 1;
    private const long DeniedEpoch = 2;
    private const long RestoredEpoch = 3;

    internal static async Task RunAsync(PhysicalShardCatalogFixture fixture)
    {
        PhysicalOwnerDirectoryWholeFlow.Bootstrap(fixture);
        var operation = PhysicalOwnerDirectoryWholeFlow.Operation(fixture,
            PhysicalOwnerDirectoryWholeFlow.Request(), PhysicalOwnerDirectoryWholeFlow.RegistrationId);
        var result = fixture.Database.Apply(operation);
        await Assert.That(result.Error).IsNull();
        await SetAdministratorAsync(fixture, true, GrantedEpoch);
        await PhysicalOwnerDirectoryReadAuthorityAssertions.LiteralAsync(fixture, Administrator);
        var directory = PhysicalOwnerDirectoryWholeFlow.DirectoryBytes(fixture);
        var catalog = fixture.ReadCatalogBytes();
        await SetAdministratorAsync(fixture, false, DeniedEpoch);
        await RefuseCorruptAuthorityAsync(fixture, directory, catalog);
        await RestoreAndReplayAsync(fixture, operation, result, directory, catalog);
    }

    private static async Task RefuseCorruptAuthorityAsync(PhysicalShardCatalogFixture fixture,
        byte[] directory, byte[] catalog)
    {
        var corrupt = PhysicalOwnerDirectoryWholeFlow.Expected() with
        { ControlOwner = PhysicalOwnerDirectoryWholeFlow.Destination.Owner };
        ReplaceDirectory(fixture, NativeSerialization.Serialize(corrupt));
        var image = PhysicalShardCatalogPolicyOutcomeAssertions.Image(fixture);
        var position = fixture.Store.Position;
        await PhysicalOwnerDirectoryReadAuthorityAssertions.DeniedAndCorruptAsync(fixture);
        await PhysicalOwnerDirectoryReadAuthorityAssertions.UnchangedAsync(fixture, image, position);
        fixture.Reopen();
        await PhysicalOwnerDirectoryReadAuthorityAssertions.DeniedAndCorruptAsync(fixture);
        await PhysicalOwnerDirectoryReadAuthorityAssertions.UnchangedAsync(fixture, image, position);
        ReplaceDirectory(fixture, directory);
        await Assert.That(PhysicalOwnerDirectoryWholeFlow.DirectoryBytes(fixture)
            .SequenceEqual(directory)).IsTrue();
        await Assert.That(fixture.ReadCatalogBytes().SequenceEqual(catalog)).IsTrue();
        var repaired = PhysicalShardCatalogPolicyOutcomeAssertions.Image(fixture);
        var repairedPosition = fixture.Store.Position;
        await PhysicalOwnerDirectoryReadAuthorityAssertions.DeniedAsync(fixture);
        await PhysicalOwnerDirectoryReadAuthorityAssertions.LiteralAsync(fixture,
            PhysicalShardCatalogFixture.RootPrincipalId);
        await PhysicalOwnerDirectoryReadAuthorityAssertions.UnchangedAsync(fixture, repaired, repairedPosition);
    }

    private static async Task RestoreAndReplayAsync(PhysicalShardCatalogFixture fixture,
        ReplicatedOperation operation, OperationResult original, byte[] directory, byte[] catalog)
    {
        await SetAdministratorAsync(fixture, true, RestoredEpoch);
        await PhysicalOwnerDirectoryReadAuthorityAssertions.LiteralAsync(fixture, Administrator);
        var image = PhysicalShardCatalogPolicyOutcomeAssertions.Image(fixture);
        var position = fixture.Store.Position;
        await Assert.That(NativeSerialization.Serialize(fixture.Database.Apply(operation))
            .SequenceEqual(NativeSerialization.Serialize(original))).IsTrue();
        await PhysicalOwnerDirectoryReadAuthorityAssertions.UnchangedAsync(fixture, image, position);
        fixture.Reopen();
        await PhysicalOwnerDirectoryReadAuthorityAssertions.UnchangedAsync(fixture, image, position);
        await PhysicalOwnerDirectoryReadAuthorityAssertions.LiteralAsync(fixture, Administrator);
        await Assert.That(NativeSerialization.Serialize(fixture.Database.Apply(operation))
            .SequenceEqual(NativeSerialization.Serialize(original))).IsTrue();
        await PhysicalOwnerDirectoryReadAuthorityAssertions.UnchangedAsync(fixture, image, position);
        await Assert.That(PhysicalOwnerDirectoryWholeFlow.DirectoryBytes(fixture)
            .SequenceEqual(directory)).IsTrue();
        await Assert.That(fixture.ReadCatalogBytes().SequenceEqual(catalog)).IsTrue();
    }

    private static async Task SetAdministratorAsync(PhysicalShardCatalogFixture fixture, bool allowed, long epoch)
    {
        var principal = new PrincipalRecord(Administrator, PhysicalOwnerDirectoryWholeFlow.TenantId, [], [])
        { ClusterAdministrator = allowed, PolicyEpoch = epoch };
        var result = fixture.Database.Apply(new(Guid.NewGuid(), OperationKind.ConfigurePrincipal,
            PhysicalShardCatalogFixture.RootPrincipalId, fixture.Database.EvaluationClock.GetUtcNow(),
            JsonSerializer.Serialize(new ConfigurePrincipalRequest(principal), JsonDefaults.Options)));
        await Assert.That(result.Error).IsNull();
        var persisted = fixture.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal(Administrator)));
        await Assert.That(NativeSerialization.Serialize(persisted).SequenceEqual(
            NativeSerialization.Serialize(principal))).IsTrue();
    }

    private static void ReplaceDirectory(PhysicalShardCatalogFixture fixture, byte[] bytes)
        => fixture.Store.Commit((transaction, _) =>
        {
            transaction.Put(PhysicalOwnerDirectorySerialization.Key(), bytes);
            return true;
        });
}
