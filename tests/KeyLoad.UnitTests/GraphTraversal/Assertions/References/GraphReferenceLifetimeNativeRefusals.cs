using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ResourceExecution;
using static KeyLoad.UnitTests.Features.GraphTraversal.GraphReferenceLifetimeNativeProtocol;

using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal static class GraphReferenceLifetimeNativeRefusals
{
    internal static async Task RunAsync(NativeDropRuntimeProfile profile, EntityRef anchor,
        EntityRef originalTarget, EntityRef freshTarget, CancellationToken token)
    {
        var database = profile.Source.Database;
        await ReadAsync(profile, () => database.Traverse(PartitionStoreProtocol.AdministratorId,
            NativeDropRuntimeProtocol.Partition, OriginalGraph, anchor with
            {
                Lifetime = null
            }, Depth, Vertices,
            Edges, cancellationToken: token));
        await ReadAsync(profile, () => database.ShortestPath(PartitionStoreProtocol.AdministratorId,
            new(GraphReferenceLifetimeNativeProtocol.Version, NativeDropRuntimeProtocol.Partition, OriginalGraph, anchor with
            {
                Lifetime = null
            },
                freshTarget with
                {
                    Lifetime = null
                }, Depth, Vertices, Edges), cancellationToken: token));
        await ReadAsync(profile, () => database.ReadIncomingGraphEdges(PartitionStoreProtocol.AdministratorId,
            new(GraphReferenceLifetimeNativeProtocol.Version, freshTarget with
            {
                Lifetime = null
            }, OriginalGraph, Edges), cancellationToken: token));
        await ReadAwaitedAsync(profile, () => GraphReferenceLifetimeNativeReads.SearchAsync(profile, OriginalGraph,
            anchor with
            {
                Lifetime = null
            }, token));
        var image = QueueWholeFlowStorage.Bytes(profile.Source.Store);
        var position = profile.Source.Store.Position;
        var command = new CommandRequest(Guid.NewGuid(), NativeDropRuntimeProtocol.Partition,
            [new UpsertEdge(FreshGraph, DeniedEdge, anchor, originalTarget, Label, Attributes)]);
        var reply = await NativeDropPublicSubmission.SubmitAsync(profile, OperationKind.Batch, command.CommandId,
            PartitionStoreProtocol.AdministratorId, NativeSerialization.Serialize(command), token);
        await Assert.That(reply.Error).IsEqualTo((ErrorCode?)ErrorCode.OwnershipLost);
        await Assert.That(reply.Payload.IsEmpty).IsTrue();
        await SameAsync(profile, image, position);
    }

    private static async Task ReadAsync<T>(NativeDropRuntimeProfile profile, Func<T> read)
    {
        var image = QueueWholeFlowStorage.Bytes(profile.Source.Store);
        var position = profile.Source.Store.Position;
        var error = Assert.ThrowsExactly<KeyLoadException>(() => _ = read());
        ArgumentNullException.ThrowIfNull(error);
        await Assert.That(error.Code).IsEqualTo(ErrorCode.OwnershipLost);
        await SameAsync(profile, image, position);
    }

    private static async Task ReadAwaitedAsync<T>(NativeDropRuntimeProfile profile, Func<Task<T>> read)
    {
        var image = QueueWholeFlowStorage.Bytes(profile.Source.Store);
        var position = profile.Source.Store.Position;
        var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(async () => _ = await read());
        ArgumentNullException.ThrowIfNull(error);
        await Assert.That(error.Code).IsEqualTo(ErrorCode.OwnershipLost);
        await SameAsync(profile, image, position);
    }

    private static async Task SameAsync(NativeDropRuntimeProfile profile, string[] image, long position)
    {
        await Assert.That(profile.Source.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(profile.Source.Store).SequenceEqual(image)).IsTrue();
    }
}
