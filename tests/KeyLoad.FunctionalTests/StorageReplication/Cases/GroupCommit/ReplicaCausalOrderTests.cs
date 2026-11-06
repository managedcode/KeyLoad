using KeyLoad.FunctionalTests.DatabaseComposition;

namespace KeyLoad.FunctionalTests.StorageReplication;

/// <summary>Exact effects preserve the native source's resource and data dependency order.</summary>
internal sealed class ReplicaCausalOrderTests
{
    private const string Table = "replica-causal-table";
    private const string Row = "first";
    private const string Json = "{\"id\":\"first\"}";
    private const string IdColumn = "id";
    private const string Graph = "replica-causal-graph";

    [Test]
    public Task ForeignVertexDecisionWaitsForItsObservedPartitionAndReplayKeepsExactEffects()
        => DatabaseCompositionReplicaCut.RunAsync(SeedGraph, async (source, replica) =>
        {
            var foreign = source.Partition with { PartitionKey = "foreign-causal-vertex" };
            var vertex = new EntityRef(foreign, Table, Row);
            var id = Guid.NewGuid();
            var written = await PublishAsync(source, OperationKind.Batch,
                new CommandRequest(id, foreign, [new PutDocument(Table, Row, Json)]), foreign, id);
            id = Guid.NewGuid();
            var linked = await PublishAsync(source, OperationKind.Batch,
                new CommandRequest(id, source.Partition, [new UpsertEdge(Graph, Row,
                    new(source.Partition, Table, Row), vertex, "related", "{}", 0)]), source.Partition, id);
            await Assert.That(linked.Dependencies).Contains(new StoragePartitionPosition(foreign, written.Position));
            var image = DatabaseCompositionReplicaCut.ReplicatedImage(replica.Store);
            var position = replica.Store.Position;
            var refused = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
                replica.Database.ApplyReplicaBatchAsync(linked, GroupCommitCommands.Token));
            await Assert.That(refused!.Code).IsEqualTo(ErrorCode.RecoveryRequired);
            await Assert.That(refused.Message).IsEqualTo(StorageReplicaDependencies.NotReady);
            await Assert.That(replica.Store.Position).IsEqualTo(position);
            await Assert.That(DatabaseCompositionReplicaCut.ReplicatedImage(replica.Store)).IsEquivalentTo(image);
            await replica.Database.ApplyReplicaBatchAsync(written, GroupCommitCommands.Token);
            await replica.Database.ApplyReplicaBatchAsync(linked, GroupCommitCommands.Token);
            await SameImageAsync(source, replica);
            position = replica.Store.Position;
            await replica.Database.ApplyReplicaBatchAsync(linked, GroupCommitCommands.Token);
            await Assert.That(replica.Store.Position).IsEqualTo(position);
            await SameImageAsync(source, replica);
            var graph = replica.Database.Traverse(GroupCommitCommands.Root, source.Partition, Graph,
                new(source.Partition, Table, Row), maxDepth: 1);
            var edge = await Assert.That(graph.Edges).HasSingleItem();
            var document = replica.Database.GetDocument(GroupCommitCommands.Root, vertex);
            await Assert.That(document!.Json).IsEqualTo(Json);
            await Assert.That(edge.To).IsEqualTo(document.Reference);
            await Assert.That(graph.Vertices.Length).IsEqualTo(2);
        });

    private static TestDatabase SeedGraph()
    {
        var source = new TestDatabase();
        source.Configure(Table, ResourceKind.Collection);
        source.Configure(Graph, ResourceKind.Graph);
        source.Commit(new PutDocument(Table, Row, Json));
        return source;
    }

    [Test]
    public async Task CatalogRemovalWaitsForEarlierDataEffectsThenBothImagesAndRecreatedTableAgree()
    {
        await DatabaseCompositionReplicaCut.RunAsync(SeedTable, async (source, replica) =>
        {
            var id = Guid.NewGuid();
            var written = await PublishAsync(source, OperationKind.Batch,
                new CommandRequest(id, source.Partition, [new PutDocument(Table, Row, Json)]), source.Partition, id);
            var removed = await PublishAsync(source, OperationKind.DropTable,
                new DropTableRequest(source.Partition.TenantId, source.Partition.DatabaseId, Table), null, Guid.NewGuid());
            var before = DatabaseCompositionReplicaCut.ReplicatedImage(replica.Store);
            var refused = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
                replica.Database.ApplyReplicaBatchAsync(removed, GroupCommitCommands.Token));
            await Assert.That(refused!.Code).IsEqualTo(ErrorCode.RecoveryRequired);
            await Assert.That(DatabaseCompositionReplicaCut.ReplicatedImage(replica.Store)).IsEquivalentTo(before);

            await replica.Database.ApplyReplicaBatchAsync(written, GroupCommitCommands.Token);
            await replica.Database.ApplyReplicaBatchAsync(removed, GroupCommitCommands.Token);
            await SameImageAsync(source, replica);
            var recreated = await PublishAsync(source, OperationKind.CreateTable,
                new CreateTableRequest(source.Partition.TenantId, source.Partition.DatabaseId, Definition(source.Partition)), null, Guid.NewGuid());
            await replica.Database.ApplyReplicaBatchAsync(recreated, GroupCommitCommands.Token);
            var nextId = Guid.NewGuid();
            var healthy = await PublishAsync(source, OperationKind.Batch,
                new CommandRequest(nextId, source.Partition, [new PutDocument(Table, Row, Json)]), source.Partition, nextId);
            await replica.Database.ApplyReplicaBatchAsync(healthy, GroupCommitCommands.Token);
            await replica.Database.ApplyReplicaBatchAsync(healthy, GroupCommitCommands.Token);
            await SameImageAsync(source, replica);
            var actual = replica.Database.GetDocument(GroupCommitCommands.Root, new(source.Partition, Table, Row));
            await Assert.That(actual!.Json).IsEqualTo(Json);
            await Assert.That(actual.Revision).IsEqualTo(1L);
        });
    }

    private static TestDatabase SeedTable()
    {
        var source = new TestDatabase();
        source.Submit(OperationKind.CreateTable,
            new CreateTableRequest(source.Partition.TenantId, source.Partition.DatabaseId, Definition(source.Partition))).Get<ResourceDefinition>();
        return source;
    }

    private static ResourceDefinition Definition(PartitionRef partition) => new(Table, ResourceKind.Collection, partition.TransactionDomainId)
    {
        RelationalSchema = new(IdColumn, [new(IdColumn, RelationalColumnType.Text)])
    };

    private static async Task<StorageReplicaBatch> PublishAsync<TRequest>(TestDatabase source, OperationKind kind,
        TRequest request, PartitionRef? partition, Guid id) where TRequest : class
    {
        var command = source.Database.CreateCommand(kind, id, GroupCommitCommands.Root,
            source.Database.EvaluationClock.GetUtcNow(), request);
        var prepared = source.Database.PrepareStorageOperations([command], GroupCommitCommands.Token);
        var before = source.Store.Read(view => StorageOperationApply.ReadCanonicalSequence(view, partition));
        var batch = await source.Database.ApplyStorageOperationsAsync(prepared, before + 1,
            GroupCommitCommands.Token, retainReplication: true);
        if (kind == OperationKind.DropTable)
        {
            _ = batch.Applied.Single().Result.Get<DropTableResult>();
        }
        await Assert.That(batch.Applied.Single().Result.IsSuccess).IsTrue();
        return batch.Replication ?? throw new InvalidOperationException("The primary did not retain its exact effects.");
    }

    private static async Task SameImageAsync(TestDatabase source, DatabaseCompositionReplicaCut replica)
        => await Assert.That(DatabaseCompositionReplicaCut.ReplicatedImage(replica.Store)).IsEquivalentTo(
            DatabaseCompositionReplicaCut.ReplicatedImage(source.Store));
}
