using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ResourceExecution;
using static KeyLoad.UnitTests.Features.GraphTraversal.GraphReferenceLifetimeNativeProtocol;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal static class GraphReferenceLifetimeNativeCalls
{
    internal static async Task SeedAsync(NativeDropRuntimeProfile profile, CancellationToken token)
    {
        await NativeDropRuntimeSeed.CreateAsync(profile, token);
        foreach (var definition in new ResourceDefinition[]
        {
            new(AnchorCollection, ResourceKind.Collection, NativeDropRuntimeProtocol.Domain),
            new(OriginalGraph, ResourceKind.Graph, NativeDropRuntimeProtocol.Domain),
            new(FreshGraph, ResourceKind.Graph, NativeDropRuntimeProtocol.Domain)
        })
        {
            var configured = await profile.Source.SubmitAsync(OperationKind.ConfigureResource, Guid.NewGuid(),
                PartitionStoreProtocol.AdministratorId, NativeSerialization.Serialize(new ConfigureResourceRequest(
                    NativeDropRuntimeProtocol.Tenant, NativeDropRuntimeProtocol.Database, definition)), token);
            await NativeDropRuntimeAssertions.SameAsync(definition, NativeDropRuntimeAssertions.Value<ResourceDefinition>(configured));
        }
        _ = await CommitAsync(profile, new(Guid.NewGuid(), NativeDropRuntimeProtocol.Partition,
            [new PutDocument(AnchorCollection, AnchorId, AnchorJson)]), token);
    }

    internal static async Task<CommitReceipt> CommitAsync(NativeDropRuntimeProfile profile,
        CommandRequest command, CancellationToken token)
        => NativeDropRuntimeAssertions.Value<CommitReceipt>(await profile.Source.SubmitAsync(OperationKind.Batch,
            command.CommandId, PartitionStoreProtocol.AdministratorId, NativeSerialization.Serialize(command), token));

    internal static EntityRef Reference(NativeDropRuntimeProfile profile, string collection, string id)
        => profile.Source.Database.GetDocument(PartitionStoreProtocol.AdministratorId,
            new(NativeDropRuntimeProtocol.Partition, collection, id))!.Reference;

    internal static CommandRequest Command(string graph, EntityRef anchor, EntityRef target)
        => new(Guid.NewGuid(), NativeDropRuntimeProtocol.Partition,
            [new UpsertEdge(graph, EdgeId, anchor with { Lifetime = null }, target with { Lifetime = null }, Label, Attributes)]);
}
