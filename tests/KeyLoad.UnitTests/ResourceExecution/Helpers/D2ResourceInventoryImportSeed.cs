using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class D2ResourceInventoryImportSeed
{
    internal static void AdditionalAuthentication(ControlledPartitionMovementNode target)
        => target.Database.Bootstrap(new(D2ResourceInventoryUnknownSeed.SecondAdministrator,
            D2ResourceInventoryData.Tenant, [], [])
        {
            ClusterAdministrator = true,
            PolicyEpoch = D2ResourceInventoryData.EmptyRevision
        }, DatabaseEngine.Credential(D2ResourceInventoryUnknownSeed.SecondAdministrator,
            D2ResourceInventoryUnknownSeed.SecondAdministrator, D2ResourceInventoryUnknownSeed.Credential));

    internal static async Task<ResourceLifetimeV1> TargetAsync(ControlledPartitionMovementNode target,
        ControlledPartitionMovementLoopbackCorpus corpus, CancellationToken token)
    {
        var definition = new ResourceDefinition(D2ResourceInventoryImportData.Resource, ResourceKind.Collection,
            D2ResourceInventoryImportData.Domain);
        var partition = D2ResourceInventoryImportData.Partition;
        var original = Operation(target, OperationKind.ConfigureResource,
            new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, definition));
        var configured = Submit(target, original, token);
        await D2ResourceInventoryAssertions.EqualAsync(definition, configured.Get<ResourceDefinition>());
        var directory = Submit(target, Operation(target, OperationKind.RegisterPhysicalOwner,
            new RegisterPhysicalOwnerV1(D2ResourceInventoryImportData.Version, D2ResourceInventoryImportData.Empty,
                corpus.Destination, corpus.Control)), token).Get<PhysicalOwnerDirectoryV1>();
        await D2ResourceInventoryAssertions.EqualAsync(corpus.Destination,
            directory.Owners.Single(entry => entry.Owner.PhysicalShardId == corpus.Destination.Owner.PhysicalShardId));
        await Assert.That(target.Journal.Log.State.LastIndex).IsEqualTo(D2ResourceInventoryImportData.TargetSetupEntries);
        return ResourceLifetimeIdentity.Create(original, partition.TenantId, partition.DatabaseId, definition);
    }

    internal static (ReplicatedOperation Operation, OperationResult Outcome) Concurrent(
        ControlledPartitionMovementNode target, CancellationToken token)
    {
        var id = Guid.NewGuid();
        var request = new CommandRequest(id, D2ResourceInventoryImportData.Partition,
            [new PutDocument(D2ResourceInventoryImportData.Resource, D2ResourceInventoryImportData.DocumentId,
                D2ResourceInventoryImportData.Json, D2ResourceInventoryImportData.Empty)]);
        var original = target.Database.CreateNativeOperation(OperationKind.Batch, id, PhysicalShardCatalogFixture.RootPrincipalId,
            target.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request));
        return (original, Submit(target, original, token));
    }

    private static ReplicatedOperation Operation<T>(ControlledPartitionMovementNode owner, OperationKind kind, T body)
        => owner.Database.CreateNativeOperation(kind, Guid.NewGuid(), PhysicalShardCatalogFixture.RootPrincipalId,
            owner.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(body));

    private static OperationResult Submit(ControlledPartitionMovementNode owner, ReplicatedOperation original, CancellationToken token)
    {
        var result = owner.Journal.Submit(original, token);
        if (result.Error is not null)
        {
            throw new InvalidOperationException("The genuine D2 image regression setup failed: " + result.Error);
        }
        return result;
    }
}
