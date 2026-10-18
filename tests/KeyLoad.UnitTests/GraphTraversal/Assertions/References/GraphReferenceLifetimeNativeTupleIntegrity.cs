using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.ResourceExecution;
using ManagedCode.Communication.Commands;
using static KeyLoad.UnitTests.Features.GraphTraversal.GraphReferenceLifetimeNativeProtocol;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

/// <summary>Proves Core.Apply corruption rollback without entering the CLUSTER append/materializer path.</summary>
internal static class GraphReferenceLifetimeNativeTupleIntegrity
{
    private const int FaultAndRepairCommits = 2;
    private const string ForeignCollection = "other";
    private const string RollbackWitness = "tuple-corruption-witness";
    private const string CorruptionDetail = "A canonical graph vertex record is inconsistent.";
    private const string MissingOriginal = "The native graph tuple fixture requires its original stored document.";

    internal static async Task VerifyAsync(NativeDropRuntimeProfile profile, string graph,
        EntityRef anchor, EntityRef target, CancellationToken token)
    {
        await Task.WhenAll(profile.Hosts.Nodes.Select(node => node.Partition.Coordinator.ReadBarrierAsync(token)));
        var store = profile.Source.Store;
        var clean = Capture(profile);
        var key = DocumentStorageKeys.RecordKey(target);
        var original = store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException(MissingOriginal);
        var record = NativeSerialization.Deserialize<DocumentRecord>(original);
        store.Commit((transaction, _) =>
        {
            transaction.PutRecord(key, record with
            {
                Reference = record.Reference with
                {
                    Collection = ForeignCollection
                }
            });
            return true;
        });
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var dirty = Capture(profile);
            await ReadsAsync(profile, graph, anchor, target, token);
            await UnchangedAsync(profile, dirty);
            await WritesAsync(profile, graph, anchor, target, token);
            await UnchangedAsync(profile, dirty);
        }, failures);
        ServerFailureObserver.Observe(() => store.Commit((transaction, _) =>
        {
            transaction.Put(key, original);
            return true;
        }), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await UnchangedAsync(profile, clean, FaultAndRepairCommits);
        await Task.WhenAll(profile.Hosts.Nodes.Select(node => node.Partition.Coordinator.ReadBarrierAsync(token)));
        await GraphReferenceLifetimeNativeReads.HealthyAsync(profile, graph, anchor, target, token);
    }

    private static async Task ReadsAsync(NativeDropRuntimeProfile profile, string graph,
        EntityRef anchor, EntityRef target, CancellationToken token)
    {
        var image = ControlledPartitionMovementRawImage.Bytes(profile.Source.Store);
        var cut = profile.Source.Store.Position;
        var traversal = Assert.ThrowsExactly<KeyLoadException>(() => profile.Source.Database.Traverse(
            PartitionStoreProtocol.AdministratorId, NativeDropRuntimeProtocol.Partition, graph,
            anchor with
            {
                Lifetime = null
            }, Depth, Vertices, Edges, cancellationToken: token));
        await CorruptionAsync(traversal);
        var path = Assert.ThrowsExactly<KeyLoadException>(() => profile.Source.Database.ShortestPath(
            PartitionStoreProtocol.AdministratorId, new(GraphReferenceLifetimeNativeProtocol.Version, NativeDropRuntimeProtocol.Partition, graph,
                anchor with
                {
                    Lifetime = null
                }, target with
                {
                    Lifetime = null
                }, Depth, Vertices, Edges), cancellationToken: token));
        await CorruptionAsync(path);
        await Assert.That(profile.Source.Store.Position).IsEqualTo(cut);
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(profile.Source.Store).SequenceEqual(image)).IsTrue();
    }

    private static async Task WritesAsync(NativeDropRuntimeProfile profile, string graph,
        EntityRef anchor, EntityRef target, CancellationToken token)
    {
        Mutation[] mutations =
        [
            new UpsertEdge(graph, EdgeId, anchor, target, Label, Attributes, Revision),
            new DeleteEdge(graph, EdgeId, Revision)
        ];
        foreach (var mutation in mutations)
        {
            var command = new CommandRequest(Guid.NewGuid(), NativeDropRuntimeProtocol.Partition,
                [new PutDocument(AnchorCollection, RollbackWitness, Attributes), mutation]);
            var operation = profile.Source.Database.CreateCommand(OperationKind.Batch, command.CommandId,
                PartitionStoreProtocol.AdministratorId, profile.Source.Clock.GetUtcNow(), NativeSerialization.Serialize(command));
            var verified = profile.Source.Database.OwnCommand(operation);
            await RefuseWriteAsync(profile, verified, token);
            await RefuseWriteAsync(profile, verified, token);
        }
    }

    private static async Task RefuseWriteAsync(NativeDropRuntimeProfile profile, Command<DatabaseCommand> verified,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var before = Capture(profile);
        var error = Assert.ThrowsExactly<KeyLoadException>(() => profile.Source.Database.Apply(verified));
        await CorruptionAsync(error);
        await UnchangedAsync(profile, before);
    }

    private static OwnerCut[] Capture(NativeDropRuntimeProfile profile)
        => profile.Hosts.Nodes.Select(node => new OwnerCut(node, node.Store.Identity.NodeId,
            node.Store.Identity.Incarnation, node.Store.Position, node.Database.LastApplied,
            NativeSerialization.Serialize(node.Log.State), ControlledPartitionMovementRawImage.Bytes(node.Store))).ToArray();

    private static async Task UnchangedAsync(NativeDropRuntimeProfile profile, OwnerCut[] expected,
        int sourceFixtureCommits = NoFixtureCommits)
    {
        await Assert.That(profile.Hosts.Nodes.Count).IsEqualTo(SixNativeOwners);
        await Assert.That(expected.Length).IsEqualTo(SixNativeOwners);
        foreach (var cut in expected)
        {
            var delta = ReferenceEquals(cut.Owner, profile.Source) ? sourceFixtureCommits : NoFixtureCommits;
            await Assert.That(cut.Owner.Store.Identity.NodeId).IsEqualTo(cut.NodeId);
            await Assert.That(cut.Owner.Store.Identity.Incarnation).IsEqualTo(cut.Incarnation);
            await Assert.That(cut.Owner.Store.Position).IsEqualTo(cut.Position + delta);
            await Assert.That(cut.Owner.Database.LastApplied).IsEqualTo(cut.Applied);
            await Assert.That(NativeSerialization.Serialize(cut.Owner.Log.State).SequenceEqual(cut.LogState)).IsTrue();
            await Assert.That(ControlledPartitionMovementRawImage.Bytes(cut.Owner.Store).SequenceEqual(cut.Image)).IsTrue();
        }
    }

    private const int NoFixtureCommits = 0;
    private const int SixNativeOwners = 6;
    private sealed record OwnerCut(ControlledPartitionMovementNativeHost Owner, Guid NodeId, Guid Incarnation,
        long Position, long Applied, byte[] LogState, string[] Image);

    private static async Task CorruptionAsync(KeyLoadException error)
    {
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(error.Message).IsEqualTo(CorruptionDetail);
    }
}
