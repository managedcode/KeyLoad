using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class D2ArchiveSeed
{
    internal static async Task<D2ArchiveSource> CreateAsync(ControlledPartitionMovementNode owner,
        RegisteredPhysicalOwnerV1 registered, RegisteredPhysicalOwnerV1 peer, int ordinal, bool unknown, CancellationToken token)
    {
        await Assert.That(owner.Store.Position).IsEqualTo(D2ArchiveData.Single);
        if (unknown)
        {
            owner.Database.Bootstrap(new(D2ArchiveData.Secondary, "d2-archive", [], [])
            {
                ClusterAdministrator = true,
                PolicyEpoch = D2ArchiveData.Empty
            }, DatabaseEngine.Credential(D2ArchiveData.Secondary, D2ArchiveData.Secondary, D2ArchiveData.SecondaryCredential));
        }
        var physical = registered.Owner;
        Submit(owner, OperationKind.BootstrapPhysicalShardCatalog,
            new BootstrapPhysicalShardCatalogRequest(D2ArchiveData.Version, D2ArchiveData.Empty,
                physical.PhysicalShardId, physical.Incarnation, physical.VoterIds), token);
        Submit(owner, OperationKind.RegisterPhysicalOwner,
            new RegisterPhysicalOwnerV1(D2ArchiveData.Version, D2ArchiveData.Empty, registered, peer), token);
        var partition = D2ArchiveData.Partition(ordinal);
        var documents = Configure(owner, partition, D2ArchiveData.Documents(ordinal), token);
        var unrelated = Configure(owner, partition, D2ArchiveData.Unrelated(ordinal), token);
        var id = Guid.NewGuid();
        var command = new CommandRequest(id, partition,
            [new PutDocument(documents.ResourceName, D2ArchiveData.SeedId, D2ArchiveData.SeedJson, D2ArchiveData.Empty),
             new PutDocument(unrelated.ResourceName, D2ArchiveData.UnrelatedId, D2ArchiveData.UnrelatedJson, D2ArchiveData.Empty)]);
        var original = new ReplicatedOperation(id, OperationKind.Batch, D2ArchiveData.Root,
            owner.Database.EvaluationClock.GetUtcNow(), JsonSerializer.Serialize(command, JsonDefaults.Options));
        var outcome = owner.Journal.Submit(original, token);
        await Assert.That(outcome.Error).IsNull();
        var expected = new CommitReceipt(id, new(physical.Incarnation, partition.AtomicPartitionId,
            D2ArchiveData.SeedEntries, physical.PlacementEpoch),
            [new(D2ArchiveData.PutDocumentKind, documents.ResourceName, D2ArchiveData.SeedId, D2ArchiveData.Single),
             new(D2ArchiveData.PutDocumentKind, unrelated.ResourceName, D2ArchiveData.UnrelatedId, D2ArchiveData.Single)],
            DurabilityProfile.ProcessDurable);
        await D2ResourceInventoryAssertions.EqualAsync(expected, outcome.Get<CommitReceipt>());
        await Assert.That(owner.Journal.Log.State.LastIndex).IsEqualTo(D2ArchiveData.SeedEntries);
        var retained = owner.Store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(partition, D2ArchiveData.Root, id)))
            ?? throw new InvalidOperationException("The genuine archived original outcome is missing.");
        var signed = owner.Journal.Log.Read(D2ArchiveData.SeedEntries, D2ArchiveData.Single,
            owner.Journal.Configuration.MaxAppendBytes).Single().Operation
            ?? throw new InvalidOperationException("The actual signed source journal entry is missing.");
        await Assert.That(signed.Id).IsEqualTo(original.Id);
        await Assert.That(signed.PayloadJson).IsEqualTo(original.PayloadJson);
        return new(ordinal, owner, registered, partition, documents, unrelated, original, signed, outcome, retained, unknown);
    }

    private static ResourceLifetimeV1 Configure(ControlledPartitionMovementNode owner, PartitionRef partition,
        string name, CancellationToken token)
    {
        var definition = new ResourceDefinition(name, ResourceKind.Collection, partition.TransactionDomainId);
        var original = Operation(owner, OperationKind.ConfigureResource, Guid.NewGuid(),
            new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, definition));
        var result = owner.Journal.Submit(original, token);
        if (result.Error is not null)
        {
            throw new InvalidOperationException("The genuine archive resource command failed.");
        }
        return ResourceLifetimeIdentity.Create(original, partition.TenantId, partition.DatabaseId, definition);
    }

    private static void Submit<T>(ControlledPartitionMovementNode owner, OperationKind kind, T body, CancellationToken token)
    {
        if (owner.Journal.Submit(Operation(owner, kind, Guid.NewGuid(), body), token).Error is not null)
        {
            throw new InvalidOperationException("The genuine archive catalog command failed.");
        }
    }

    private static ReplicatedOperation Operation<T>(ControlledPartitionMovementNode owner, OperationKind kind, Guid id, T body)
        => owner.Database.CreateNativeOperation(kind, id, D2ArchiveData.Root, owner.Database.EvaluationClock.GetUtcNow(),
            NativeSerialization.Serialize(body));
}
