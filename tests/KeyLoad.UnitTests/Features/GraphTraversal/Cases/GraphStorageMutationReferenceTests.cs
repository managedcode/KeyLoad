using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal sealed class GraphStorageMutationReferenceTests
{
    [Test]
    public async Task Kl022SeededMutationsAndRetriesMatchIndependentAdjacencyStateBeforeAndAfterNativeReopen()
    {
        using var database = new TestDatabase();
        GraphStorageReferenceFixture.Configure(database);
        var reference = new Dictionary<string, GraphStorageExpectedEdge>(StringComparer.Ordinal);
        var revisions = new Dictionary<string, long>(StringComparer.Ordinal);
        for (var step = 0; step < GraphStorageReferenceFixture.Steps; step++)
        {
            var id = GraphStorageReferenceFixture.EdgeId(step % GraphStorageReferenceFixture.EdgeCount);
            var previousRevision = revisions.GetValueOrDefault(id);
            var delete = step % 4 == 3 && reference.ContainsKey(id);
            var (from, to) = GraphStorageReferenceFixture.Endpoints(step);
            var json = GraphStorageReferenceFixture.Attributes(step);
            var label = step % 3 == 0 ? GraphStorageReferenceFixture.Label : "related";
            Mutation mutation = delete ? new DeleteEdge(GraphStorageReferenceFixture.Graph, id, previousRevision)
                : new UpsertEdge(GraphStorageReferenceFixture.Graph, id,
                    GraphStorageReferenceFixture.Vertex(database.Partition, from), GraphStorageReferenceFixture.Vertex(database.Partition, to),
                    label, json, previousRevision);
            var command = GraphStorageReferenceFixture.Command(database.Partition, mutation);
            var receipt = GraphStorageReferenceFixture.Submit(database.Database, command).Get<CommitReceipt>();
            revisions[id] = previousRevision + 1;
            if (delete)
            { reference.Remove(id); }
            else
            { reference[id] = new(id, from, to, label, json, previousRevision + 1); }
            await GraphStorageReferenceAssertions.VerifyAsync(database.Database, database.Partition, reference);
            var position = database.Store.Position;
            var retry = GraphStorageReferenceFixture.Submit(database.Database, command).Get<CommitReceipt>();
            await Assert.That(retry.Token).IsEqualTo(receipt.Token);
            await Assert.That(database.Store.Position).IsEqualTo(position);
            var changed = command with { Mutations = [new PutDocument(GraphStorageReferenceFixture.Nodes, "forbidden", "{}")] };
            await Assert.That(GraphStorageReferenceFixture.Submit(database.Database, changed).Error).IsEqualTo(ErrorCode.Conflict);
            await Assert.That(database.Database.GetDocument(GraphStorageReferenceFixture.Root,
                new(database.Partition, GraphStorageReferenceFixture.Nodes, "forbidden"))).IsNull();
            await Assert.That(database.Store.Position).IsEqualTo(position);
            await GraphStorageReferenceAssertions.VerifyAsync(database.Database, database.Partition, reference);
        }
        database.Store.Dispose();
        using var reopened = new ZoneTreeStore(new(database.Directory), UnitExecutionOptions.StorageExecution(),
            UnitExecutionOptions.PointCacheExecution());
        var owner = GraphStorageReferenceFixture.ReopenedOwner(reopened);
        await GraphStorageReferenceAssertions.VerifyAsync(owner, database.Partition, reference);
        var healthy = GraphStorageReferenceFixture.Command(database.Partition,
            new UpsertEdge(GraphStorageReferenceFixture.Graph, GraphStorageReferenceFixture.HealthyEdge,
                GraphStorageReferenceFixture.Vertex(database.Partition, GraphStorageReferenceFixture.VertexId(0)),
                GraphStorageReferenceFixture.Vertex(database.Partition, GraphStorageReferenceFixture.VertexId(1)),
                GraphStorageReferenceFixture.Label, "{}", 0));
        GraphStorageReferenceFixture.Submit(owner, healthy).Get<CommitReceipt>();
        reference[GraphStorageReferenceFixture.HealthyEdge] = new(GraphStorageReferenceFixture.HealthyEdge, GraphStorageReferenceFixture.VertexId(0),
            GraphStorageReferenceFixture.VertexId(1), GraphStorageReferenceFixture.Label, "{}", 1);
        await GraphStorageReferenceAssertions.VerifyAsync(owner, database.Partition, reference);
    }
}
