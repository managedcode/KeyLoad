using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class D2ResourceInventoryUnknownSeed
{
    internal const string SecondAdministrator = "d2-second-bootstrap";
    internal const string Credential = SecondAdministrator + ".real-native-credential-32-characters";
    internal const string FollowupId = "followup";

    internal static async Task<(ReplicatedOperation Operation, OperationResult Outcome)> CreateAsync(
        D2ResourceInventoryFixture fixture, CancellationToken token)
    {
        await Assert.That(fixture.Owner.Store.Position).IsEqualTo(D2ResourceInventoryData.InitialRevision);
        var principal = new PrincipalRecord(SecondAdministrator, D2ResourceInventoryData.Tenant, [], [])
        {
            ClusterAdministrator = true,
            PolicyEpoch = D2ResourceInventoryData.EmptyRevision
        };
        fixture.Database.Bootstrap(principal, DatabaseEngine.Credential(SecondAdministrator, SecondAdministrator, Credential));
        await Assert.That(fixture.Owner.Store.Position).IsGreaterThan(D2ResourceInventoryData.InitialRevision);
        await Assert.That(fixture.Database.Authenticate(Credential, fixture.Database.EvaluationClock.GetUtcNow()))
            .IsEqualTo(SecondAdministrator);
        var owner = D2ResourceInventoryData.Control.Owner;
        fixture.Submit(fixture.Operation(OperationKind.BootstrapPhysicalShardCatalog,
            new BootstrapPhysicalShardCatalogRequest(D2ResourceInventoryData.Version,
                D2ResourceInventoryData.EmptyRevision, owner.PhysicalShardId, owner.Incarnation, owner.VoterIds)), token);
        var directory = fixture.Submit(fixture.Operation(OperationKind.RegisterPhysicalOwner,
            new RegisterPhysicalOwnerV1(D2ResourceInventoryData.Version, D2ResourceInventoryData.EmptyRevision,
                D2ResourceInventoryData.Control, D2ResourceInventoryData.Destination)), token).Get<PhysicalOwnerDirectoryV1>();
        await D2ResourceInventoryAssertions.EqualAsync(D2ResourceInventoryData.Control,
            directory.Owners.Single(entry => entry.Owner.PhysicalShardId == owner.PhysicalShardId));
        await D2ResourceInventorySeeding.PolicyAsync(fixture, true, D2ResourceInventoryData.InitialEpoch, token);
        var definition = new ResourceDefinition(D2ResourceInventoryData.A, ResourceKind.Collection, D2ResourceInventoryData.Domain);
        var configured = fixture.Submit(fixture.Operation(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(D2ResourceInventoryData.Tenant, D2ResourceInventoryData.Database, definition)), token);
        await D2ResourceInventoryAssertions.EqualAsync(definition, configured.Get<ResourceDefinition>());
        return Write(fixture, D2ResourceInventoryData.DocumentId, D2ResourceInventoryData.Json[0], token);
    }

    internal static (ReplicatedOperation Operation, OperationResult Outcome) Write(D2ResourceInventoryFixture fixture,
        string documentId, string json, CancellationToken token)
    {
        var id = Guid.NewGuid();
        var command = new CommandRequest(id, D2ResourceInventoryData.Partitions[0],
            [new PutDocument(D2ResourceInventoryData.A, documentId, json, D2ResourceInventoryData.EmptyRevision)]);
        var original = fixture.Database.CreateNativeOperation(OperationKind.Batch, id, D2ResourceInventoryData.Root,
            fixture.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(command));
        return (original, fixture.Submit(original, token));
    }
}
