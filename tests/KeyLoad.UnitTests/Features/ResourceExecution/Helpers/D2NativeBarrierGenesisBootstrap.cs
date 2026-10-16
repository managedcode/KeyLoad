using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class D2NativeBarrierGenesisBootstrap
{
    private const long BootstrapTerm = 1;
    private const long ReplicaEntryStep = 1;
    private const long BarrierAndCatalogCommits = 2;
    internal static async Task<ResourceLifetimeV1> RunAsync(D2ResourceInventoryFixture fixture,
        bool secondAuthentication, bool firstCommand, CancellationToken token)
    {
        var owner = D2ResourceInventoryData.Control.Owner;
        await Assert.That(fixture.Owner.Store.Position).IsEqualTo(D2ResourceInventoryData.InitialRevision);
        var additional = new PrincipalRecord(D2ResourceInventoryUnknownSeed.SecondAdministrator,
            D2ResourceInventoryData.Tenant, [], [])
        {
            ClusterAdministrator = true,
            PolicyEpoch = D2ResourceInventoryData.EmptyRevision
        };
        if (secondAuthentication)
        {
            fixture.Database.Bootstrap(additional, DatabaseEngine.Credential(additional.Id,
                additional.Id, D2ResourceInventoryUnknownSeed.Credential));
        }
        if (firstCommand)
        {
            _ = fixture.Submit(fixture.Operation(OperationKind.ConfigurePrincipal,
                new ConfigurePrincipalRequest(additional with
                {
                    PolicyEpoch = D2ResourceInventoryData.InitialRevision
                })), token).Get<PrincipalRecord>();
        }
        var log = fixture.Owner.Journal.Log;
        if (log.State.Term < BootstrapTerm)
        {
            log.SaveTermAndVote(BootstrapTerm, owner.VoterIds.First());
        }
        var barrier = checked(log.State.LastIndex + ReplicaEntryStep);
        log.Append([new(barrier, log.State.Term, null)]);
        var position = fixture.Owner.Store.Position;
        fixture.Submit(fixture.Operation(OperationKind.BootstrapPhysicalShardCatalog,
            new BootstrapPhysicalShardCatalogRequest(D2ResourceInventoryData.Version,
                D2ResourceInventoryData.EmptyRevision, owner.PhysicalShardId, owner.Incarnation, owner.VoterIds)), token);
        await Assert.That(log.ReadEntry(barrier)!.Operation).IsNull();
        await Assert.That(fixture.Owner.Store.Position).IsEqualTo(position + BarrierAndCatalogCommits);
        await Assert.That(fixture.Database.LastApplied).IsEqualTo(barrier + ReplicaEntryStep);
        fixture.Submit(fixture.Operation(OperationKind.RegisterPhysicalOwner,
            new RegisterPhysicalOwnerV1(D2ResourceInventoryData.Version, D2ResourceInventoryData.EmptyRevision,
                D2ResourceInventoryData.Control, D2ResourceInventoryData.Destination)), token);
        await D2ResourceInventorySeeding.PolicyAsync(fixture, true, D2ResourceInventoryData.InitialEpoch, token);
        var definition = new ResourceDefinition(D2ResourceInventoryData.A, ResourceKind.Collection, D2ResourceInventoryData.Domain);
        var creation = fixture.Operation(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(D2ResourceInventoryData.Tenant, D2ResourceInventoryData.Database, definition));
        await D2ResourceInventoryAssertions.EqualAsync(definition, fixture.Submit(creation, token).Get<ResourceDefinition>());
        return fixture.Owner.Store.Read(view => ResourceLifetimeCatalog.Read(view, D2ResourceInventoryData.Tenant,
            D2ResourceInventoryData.Database, definition.Name, definition))
            ?? throw ResourceOwnerInventoryValidation.Invalid();
    }
}
