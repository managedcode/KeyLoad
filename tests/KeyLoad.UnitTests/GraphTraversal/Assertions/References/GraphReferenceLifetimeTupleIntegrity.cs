using KeyLoad.Core.DocumentStorage;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Messaging;
using KeyLoad.UnitTests.ResourceExecution;
using ManagedCode.Communication.Commands;
using static KeyLoad.UnitTests.GraphTraversal.GraphReferenceLifetimeProtocol;

namespace KeyLoad.UnitTests.GraphTraversal;

/// <summary>Proves actual local corruption rollback and repaired storage while every canonical copy retains its cut.</summary>
internal static class GraphReferenceLifetimeTupleIntegrity
{
    private const int FaultAndRepairCommits = 2;
    private const string ForeignCollection = "other";
    private const string RollbackWitness = "tuple-corruption-witness";
    private const string CorruptionDetail = "A canonical graph vertex record is inconsistent.";
    private const string MissingOriginal = "The execution graph tuple fixture requires its original stored document.";

    internal static async Task VerifyAsync(DropRuntimeProfile profile, string graph,
        EntityRef anchor, EntityRef target, CancellationToken token)
    {
        await Task.WhenAll(profile.Hosts.Nodes.Select(node => node.Database.Coordinator.ReadBarrierAsync(token)));
        var store = profile.Source.Store;
        var clean = Capture(profile);
        var key = DocumentStorageKeys.RecordKey(target);
        var original = store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException(MissingOriginal);
        var record = OrleansSerialization.Deserialize<DocumentRecord>(original);
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
        await Task.WhenAll(profile.Hosts.Nodes.Select(node => node.Database.Coordinator.ReadBarrierAsync(token)));
        await GraphReferenceLifetimeReads.HealthyAsync(profile, graph, anchor, target, token);
    }

    private static async Task ReadsAsync(DropRuntimeProfile profile, string graph,
        EntityRef anchor, EntityRef target, CancellationToken token)
    {
        var image = QueueWholeFlowStorage.Bytes(profile.Source.Store);
        var cut = profile.Source.Store.Position;
        var traversal = Assert.ThrowsExactly<KeyLoadException>(() => profile.Source.Database.Traverse(
            PartitionStoreProtocol.AdministratorId, DropRuntimeProtocol.Partition, graph,
            anchor with
            {
                Lifetime = null
            }, Depth, Vertices, Edges, cancellationToken: token));
        await CorruptionAsync(traversal);
        var path = Assert.ThrowsExactly<KeyLoadException>(() => profile.Source.Database.ShortestPath(
            PartitionStoreProtocol.AdministratorId, new(GraphReferenceLifetimeProtocol.Version, DropRuntimeProtocol.Partition, graph,
                anchor with
                {
                    Lifetime = null
                }, target with
                {
                    Lifetime = null
                }, Depth, Vertices, Edges), cancellationToken: token));
        await CorruptionAsync(path);
        await Assert.That(profile.Source.Store.Position).IsEqualTo(cut);
        await Assert.That(QueueWholeFlowStorage.Bytes(profile.Source.Store).SequenceEqual(image)).IsTrue();
    }

    private static async Task WritesAsync(DropRuntimeProfile profile, string graph,
        EntityRef anchor, EntityRef target, CancellationToken token)
    {
        Mutation[] mutations =
        [
            new UpsertEdge(graph, EdgeId, anchor, target, Label, Attributes, Revision),
            new DeleteEdge(graph, EdgeId, Revision)
        ];
        foreach (var mutation in mutations)
        {
            var command = new CommandRequest(Guid.NewGuid(), DropRuntimeProtocol.Partition,
                [new PutDocument(AnchorCollection, RollbackWitness, Attributes), mutation]);
            var operation = profile.Source.Database.CreateCommand(OperationKind.Batch, command.CommandId,
                PartitionStoreProtocol.AdministratorId, profile.Node.Clock.GetUtcNow(), OrleansSerialization.Serialize(command));
            var verified = profile.Source.Database.OwnCommand(operation);
            await RefuseWriteAsync(profile, verified, token);
            await RefuseWriteAsync(profile, verified, token);
        }
    }

    private static async Task RefuseWriteAsync(DropRuntimeProfile profile, Command<DatabaseCommand> verified,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var before = Capture(profile);
        var error = Assert.ThrowsExactly<KeyLoadException>(() => profile.Source.Database.Apply(verified));
        await CorruptionAsync(error);
        await UnchangedAsync(profile, before);
    }

    private static OwnerCut[] Capture(DropRuntimeProfile profile)
        => profile.Hosts.Nodes.Select(node =>
        {
            var owner = node.Database;
            var cut = owner.ReadCanonicalCut();
            return new OwnerCut(owner, owner.Store.Identity.NodeId, owner.Store.Identity.Incarnation,
                cut.StorePosition, cut.AppliedPosition, QueueWholeFlowStorage.Bytes(owner.Store));
        }).ToArray();

    private static async Task UnchangedAsync(DropRuntimeProfile profile, OwnerCut[] expected,
        int sourceFixtureCommits = 0)
    {
        await Assert.That(profile.Hosts.Nodes.Length).IsEqualTo(DropRuntimeProtocol.RuntimeNodes);
        await Assert.That(expected.Length).IsEqualTo(DropRuntimeProtocol.RuntimeNodes);
        foreach (var cut in expected)
        {
            var delta = ReferenceEquals(cut.Owner, profile.Source) ? sourceFixtureCommits : 0;
            await Assert.That(cut.Owner.Store.Identity.NodeId).IsEqualTo(cut.NodeId);
            await Assert.That(cut.Owner.Store.Identity.Incarnation).IsEqualTo(cut.Incarnation);
            await Assert.That(cut.Owner.Store.Position).IsEqualTo(cut.Position + delta);
            await Assert.That(cut.Owner.Database.LastApplied).IsEqualTo(cut.Applied);
            await Assert.That(QueueWholeFlowStorage.Bytes(cut.Owner.Store).SequenceEqual(cut.Image)).IsTrue();
        }
    }

    private sealed record OwnerCut(RequestCqrsHostedDatabase Owner, Guid NodeId, Guid Incarnation,
        long Position, long Applied, string[] Image);

    private static async Task CorruptionAsync(KeyLoadException error)
    {
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(error.Message).IsEqualTo(CorruptionDetail);
    }
}
