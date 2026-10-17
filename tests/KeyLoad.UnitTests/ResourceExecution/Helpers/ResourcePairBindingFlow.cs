using KeyLoad.Core;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourcePairBindingProtocol;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourcePairBindingFlow
{
    internal static async Task RunAsync(ResourcePairBindingState state, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Configure(state);
        await Assert.That(state.ReplicaIdentity.NodeId).IsNotEqualTo(state.Source.Store.Identity.NodeId);
        await Assert.That(state.ReplicaIdentity.Incarnation).IsEqualTo(state.Source.Store.Identity.Incarnation);
        await Assert.That(state.ReplicaIdentity.SigningKey.Span.SequenceEqual(state.Source.Store.Identity.SigningKey.Span)).IsTrue();
        await ResourcePairBindingBatch.RunAsync(state, token);
        await ResourcePairBindingBlob.RunAsync(state, token);
        await ResourcePairBindingSql.RunAsync(state, token);
        await ResourcePairBindingQuery.RunAsync(state, token);
        await ResourcePairBindingBm25.RunAsync(state, token);
        await ResourcePairBindingAssertions.PairsAsync(state, state.Source.Store);
        await ResourcePairBindingCold.RunAsync(state, token);
    }

    private static void Configure(ResourcePairBindingState state)
    {
        var source = state.Source;
        ResourceDefinition[] definitions =
        [
            new(Documents, ResourceKind.Collection, source.Partition.TransactionDomainId),
            new(Events, ResourceKind.StreamSet, source.Partition.TransactionDomainId),
            new(Queue, ResourceKind.WorkQueue, source.Partition.TransactionDomainId) { QueuePolicy = new() },
            new(Blobs, ResourceKind.BlobStore, source.Partition.TransactionDomainId),
            new(Table, ResourceKind.Collection, source.Partition.TransactionDomainId)
            {
                Authority = DocumentAuthority.Document,
                RelationalSchema = new(IdColumn, [new(IdColumn, RelationalColumnType.Text), new(ValueColumn, RelationalColumnType.WholeNumber)])
            }
        ];
        foreach (var definition in definitions)
        {
            _ = source.Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(source.Partition.TenantId,
                source.Partition.DatabaseId, definition)).Get<ResourceDefinition>();
            state.Pairs.Add(definition.Name, (ResourceIncarnationAssertions.Bytes(source.Store,
                KeySpace.Resource(source.Partition.TenantId, source.Partition.DatabaseId, definition.Name)),
                ResourceIncarnationAssertions.Bytes(source.Store, ResourcePairBindingFaults.LifetimeKey(source.Partition, definition.Name))));
        }
    }
}
