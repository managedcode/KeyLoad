using KeyLoad.Server;
using KeyLoad.UnitTests.ResourceExecution;
using static KeyLoad.UnitTests.GraphTraversal.GraphReferenceLifetimeProtocol;

namespace KeyLoad.UnitTests.GraphTraversal;

internal static class GraphReferenceLifetimeCalls
{
    internal static async Task SeedAsync(DropRuntimeProfile profile, CancellationToken token)
    {
        await DropRuntimeSeed.CreateAsync(profile, token);
        foreach (var definition in new ResourceDefinition[]
        {
            new(AnchorCollection, ResourceKind.Collection, DropRuntimeProtocol.Domain),
            new(OriginalGraph, ResourceKind.Graph, DropRuntimeProtocol.Domain),
            new(FreshGraph, ResourceKind.Graph, DropRuntimeProtocol.Domain)
        })
        {
            var configured = await DropPublicSubmission.SubmitAsync(profile, OperationKind.ConfigureResource, Guid.NewGuid(),
                PartitionStoreProtocol.AdministratorId, OrleansSerialization.Serialize(new ConfigureResourceRequest(
                    DropRuntimeProtocol.Tenant, DropRuntimeProtocol.Database, definition)), token);
            await DropRuntimeAssertions.SameAsync(definition, DropRuntimeAssertions.Value<ResourceDefinition>(configured));
        }
        _ = await CommitAsync(profile, new(Guid.NewGuid(), DropRuntimeProtocol.Partition,
            [new PutDocument(AnchorCollection, AnchorId, AnchorJson)]), token);
    }

    internal static async Task<CommitReceipt> CommitAsync(DropRuntimeProfile profile,
        CommandRequest command, CancellationToken token)
        => DropRuntimeAssertions.Value<CommitReceipt>(await DropPublicSubmission.SubmitAsync(profile, OperationKind.Batch,
            command.CommandId, PartitionStoreProtocol.AdministratorId, OrleansSerialization.Serialize(command), token));

    internal static EntityRef Reference(DropRuntimeProfile profile, string collection, string id)
        => profile.Source.Database.GetDocument(PartitionStoreProtocol.AdministratorId,
            new(DropRuntimeProtocol.Partition, collection, id))!.Reference;

    internal static CommandRequest Command(string graph, EntityRef anchor, EntityRef target)
        => new(Guid.NewGuid(), DropRuntimeProtocol.Partition,
            [new UpsertEdge(graph, EdgeId, anchor with { Lifetime = null }, target with { Lifetime = null }, Label, Attributes)]);
}
