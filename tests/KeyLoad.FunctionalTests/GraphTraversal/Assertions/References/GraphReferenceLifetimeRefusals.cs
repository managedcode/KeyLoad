using KeyLoad.FunctionalTests.Messaging;
using KeyLoad.FunctionalTests.ResourceExecution;
using KeyLoad.Server;
using KeyLoad.Storage;
using static KeyLoad.FunctionalTests.GraphTraversal.GraphReferenceLifetimeProtocol;

namespace KeyLoad.FunctionalTests.GraphTraversal;

internal static class GraphReferenceLifetimeRefusals
{
    internal static async Task RunAsync(DropRuntimeProfile profile, EntityRef anchor,
        EntityRef originalTarget, EntityRef freshTarget, CancellationToken token)
    {
        var database = profile.Source.Database;
        await ReadAsync(profile, () => database.Traverse(PartitionStoreProtocol.AdministratorId,
            DropRuntimeProtocol.Partition, OriginalGraph, anchor with
            {
                Lifetime = null
            }, Depth, Vertices,
            Edges, cancellationToken: token));
        await ReadAsync(profile, () => database.ShortestPath(PartitionStoreProtocol.AdministratorId,
            new(GraphReferenceLifetimeProtocol.Version, DropRuntimeProtocol.Partition, OriginalGraph, anchor with
            {
                Lifetime = null
            },
                freshTarget with
                {
                    Lifetime = null
                }, Depth, Vertices, Edges), cancellationToken: token));
        await ReadAsync(profile, () => database.ReadIncomingGraphEdges(PartitionStoreProtocol.AdministratorId,
            new(GraphReferenceLifetimeProtocol.Version, freshTarget with
            {
                Lifetime = null
            }, OriginalGraph, Edges), cancellationToken: token));
        await ReadAwaitedAsync(profile, () => GraphReferenceLifetimeReads.SearchAsync(profile, OriginalGraph,
            anchor with
            {
                Lifetime = null
            }, token));
        await RefuseCommandAsync(profile, anchor, originalTarget, token);
    }

    private static async Task RefuseCommandAsync(DropRuntimeProfile profile, EntityRef anchor,
        EntityRef originalTarget, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), DropRuntimeProtocol.Partition,
            [new UpsertEdge(FreshGraph, DeniedEdge, anchor, originalTarget, Label, Attributes)]);
        var image = CommandDomain(profile, command.CommandId);
        var position = profile.Source.Store.Position;
        var canonical = profile.Source.Database.LastApplied;
        var payload = OrleansSerialization.Serialize(command);
        var reply = await DropPublicSubmission.SubmitAsync(profile, OperationKind.Batch, command.CommandId,
            PartitionStoreProtocol.AdministratorId, payload, token);
        await Assert.That(reply.Error).IsEqualTo((ErrorCode?)ErrorCode.OwnershipLost);
        await Assert.That(reply.Payload.IsEmpty).IsTrue();
        await Assert.That(profile.Source.Store.Position).IsEqualTo(position + 1);
        await Assert.That(profile.Source.Database.LastApplied).IsEqualTo(canonical + 1);
        await Assert.That(CommandDomain(profile, command.CommandId).SequenceEqual(image)).IsTrue();
        var outcome = profile.Source.Store.Read(view => view.GetRecord<StoredOutcome>(KeySpace.PartitionOutcome(
            DropRuntimeProtocol.Partition, PartitionStoreProtocol.AdministratorId, command.CommandId)));
        await Assert.That(outcome).IsNotNull();
        await Assert.That(Errors.Code(outcome!.Result.Problem).Value).IsEqualTo(ErrorCode.OwnershipLost);
        var committed = QueueWholeFlowStorage.Bytes(profile.Source.Store);
        var replay = await DropPublicSubmission.SubmitAsync(profile, OperationKind.Batch, command.CommandId,
            PartitionStoreProtocol.AdministratorId, payload, token);
        await Assert.That(replay.Error).IsEqualTo(reply.Error);
        await Assert.That(replay.Payload.IsEmpty).IsTrue();
        await SameAsync(profile, committed, position + 1);
    }

    private static string[] CommandDomain(DropRuntimeProfile profile, Guid id)
    {
        byte[][] journal = [KeySpace.PartitionOutcome(DropRuntimeProtocol.Partition, PartitionStoreProtocol.AdministratorId, id),
            KeySpace.OutcomeLocator(DropRuntimeProtocol.Partition, PartitionStoreProtocol.AdministratorId, id),
            KeySpace.Clock.ToArray(), KeySpace.AppliedBytes, KeySpace.LastCommandBytes];
        var prefixes = journal.Select(key => Convert.ToHexString(key) + ":").ToArray();
        return QueueWholeFlowStorage.Bytes(profile.Source.Store)
            .Where(row => !prefixes.Any(prefix => row.StartsWith(prefix, StringComparison.Ordinal))).ToArray();
    }

    private static async Task ReadAsync<T>(DropRuntimeProfile profile, Func<T> read)
    {
        var image = QueueWholeFlowStorage.Bytes(profile.Source.Store);
        var position = profile.Source.Store.Position;
        var error = Assert.ThrowsExactly<KeyLoadException>(() => _ = read());
        ArgumentNullException.ThrowIfNull(error);
        await Assert.That(error.Code).IsEqualTo(ErrorCode.OwnershipLost);
        await SameAsync(profile, image, position);
    }

    private static async Task ReadAwaitedAsync<T>(DropRuntimeProfile profile, Func<Task<T>> read)
    {
        var image = QueueWholeFlowStorage.Bytes(profile.Source.Store);
        var position = profile.Source.Store.Position;
        var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(async () => _ = await read());
        ArgumentNullException.ThrowIfNull(error);
        await Assert.That(error.Code).IsEqualTo(ErrorCode.OwnershipLost);
        await SameAsync(profile, image, position);
    }

    private static async Task SameAsync(DropRuntimeProfile profile, string[] image, long position)
    {
        await Assert.That(profile.Source.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(profile.Source.Store).SequenceEqual(image)).IsTrue();
    }
}
