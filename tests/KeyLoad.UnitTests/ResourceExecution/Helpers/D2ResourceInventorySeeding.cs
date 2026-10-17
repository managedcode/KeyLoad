using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class D2ResourceInventorySeeding
{
    internal static async Task<D2ResourceInventorySeed> CreateAsync(D2ResourceInventoryFixture fixture, CancellationToken token)
    {
        var owner = D2ResourceInventoryData.Control.Owner;
        await Assert.That(fixture.Owner.Store.Position).IsEqualTo(D2ResourceInventoryData.InitialRevision);
        await Assert.That(fixture.Owner.Store.Identity.Incarnation).IsEqualTo(owner.Incarnation);
        await Assert.That(fixture.Owner.Journal.Log.State.LastIndex).IsEqualTo(D2ResourceInventoryData.EmptyRevision);
        fixture.Submit(fixture.Operation(OperationKind.BootstrapPhysicalShardCatalog,
            new BootstrapPhysicalShardCatalogRequest(D2ResourceInventoryData.Version, D2ResourceInventoryData.EmptyRevision, owner.PhysicalShardId, owner.Incarnation, owner.VoterIds)), token);
        var registered = fixture.Submit(fixture.Operation(OperationKind.RegisterPhysicalOwner,
            new RegisterPhysicalOwnerV1(D2ResourceInventoryData.Version, D2ResourceInventoryData.EmptyRevision, D2ResourceInventoryData.Control, D2ResourceInventoryData.Destination)), token)
            .Get<PhysicalOwnerDirectoryV1>();
        await PolicyAsync(fixture, true, D2ResourceInventoryData.InitialEpoch, token);
        var a = await ConfigureAsync(fixture, D2ResourceInventoryData.A, D2ResourceInventoryData.Domain, token);
        var b = await ConfigureAsync(fixture, D2ResourceInventoryData.B, D2ResourceInventoryData.Domain, token);
        var writes = new ReplicatedOperation[D2ResourceInventoryData.Partitions.Length];
        var results = new OperationResult[writes.Length];
        for (var index = 0; index < writes.Length; index++)
        {
            var id = Guid.NewGuid();
            var request = new CommandRequest(id, D2ResourceInventoryData.Partitions[index],
                [new PutDocument(D2ResourceInventoryData.Collections[index], D2ResourceInventoryData.DocumentId,
                    D2ResourceInventoryData.Json[index], D2ResourceInventoryData.EmptyRevision)]);
            writes[index] = fixture.Database.CreateNativeOperation(OperationKind.Batch, id, D2ResourceInventoryData.Root,
                fixture.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request));
            results[index] = fixture.Submit(writes[index], token);
            var receipt = results[index].Get<CommitReceipt>();
            await Assert.That(receipt.CommandId).IsEqualTo(id);
            await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(request.Partition.AtomicPartitionId);
        }
        var c = await ConfigureAsync(fixture, D2ResourceInventoryData.C, D2ResourceInventoryData.OtherDomain, token);
        var observed = registered.Owners.Single(entry => entry.Owner.PhysicalShardId == owner.PhysicalShardId);
        await D2ResourceInventoryAssertions.EqualAsync(D2ResourceInventoryData.Control, observed);
        return new(a, b, c, observed, writes, results);
    }

    internal static async Task PolicyAsync(D2ResourceInventoryFixture fixture, bool allowed, long epoch, CancellationToken token)
    {
        var principal = new PrincipalRecord(D2ResourceInventoryData.Administrator, D2ResourceInventoryData.Tenant, [], [])
        {
            ClusterAdministrator = allowed,
            PolicyEpoch = epoch
        };
        fixture.Submit(fixture.Operation(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal)), token);
        var actual = fixture.Owner.Store.Read(view => view.GetRecord<PrincipalRecord>(
            KeySpace.Principal(D2ResourceInventoryData.Administrator)));
        await D2ResourceInventoryAssertions.EqualAsync(principal, actual);
    }

    private static async Task<ResourceLifetimeV1> ConfigureAsync(D2ResourceInventoryFixture fixture,
        string name, string domain, CancellationToken token)
    {
        var definition = new ResourceDefinition(name, ResourceKind.Collection, domain);
        var operation = fixture.Operation(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(D2ResourceInventoryData.Tenant, D2ResourceInventoryData.Database, definition));
        var actualDefinition = fixture.Submit(operation, token).Get<ResourceDefinition>();
        await D2ResourceInventoryAssertions.EqualAsync(definition, actualDefinition);
        var actual = fixture.Owner.Store.Read(view => ResourceLifetimeCatalog.Read(view,
            D2ResourceInventoryData.Tenant, D2ResourceInventoryData.Database, name, actualDefinition))
            ?? throw new InvalidOperationException("The genuine configured D2 lifetime is missing.");
        await D2ResourceInventoryAssertions.EqualAsync(ResourceLifetimeIdentity.Create(operation,
            D2ResourceInventoryData.Tenant, D2ResourceInventoryData.Database, definition), actual);
        return actual;
    }
}
