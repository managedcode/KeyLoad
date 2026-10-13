using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal static class GraphIncomingVertexAuthorityFlow
{
    internal static async Task Run(bool source)
    {
        using var database = new TestDatabase();
        var state = Seed(database);
        var token = TestContext.Current!.Execution.CancellationToken;
        await GraphIncomingVertexAuthorityAssertions.Healthy(database.Database, database.Store, state, null, token);
        var fault = GraphVertexAuthorityFault.Install(database.Store, state, source);
        await GraphIncomingVertexAuthorityAssertions.Refuse(database.Database, database.Store, state, token);
        await GraphIncomingVertexAuthorityAssertions.Refuse(database.Database, database.Store, state, token);
        var dirtyImage = GraphShortestPathFullStoreImage.Bytes(database.Store);
        var dirtyCut = database.Store.Position;
        database.Store.Dispose();
        var next = state.DamagedEdge with { Id = "zz-incoming-continuation" };
        var command = GraphStorageReferenceFixture.Command(database.Partition,
            new PutDocument(GraphTraversalIntegrityFixture.Nodes, "incoming-witness", "{}"),
            GraphTraversalIntegrityFixture.Mutation(next));
        CommitReceipt receipt;
        string[] finalImage;
        long finalCut;
        using (var cold = new ZoneTreeStore(new(database.Directory), database.StorageExecution, UnitExecutionOptions.PointCacheExecution()))
        {
            var owner = database.CreateConfiguredDatabase(cold);
            await GraphTraversalIntegrityAssertions.Image(cold, dirtyImage, dirtyCut);
            await GraphIncomingVertexAuthorityAssertions.Refuse(owner, cold, state, token);
            GraphTraversalIntegrityFaultRows.Repair(cold, fault);
            await GraphTraversalIntegrityAssertions.Image(cold, GraphVertexAuthorityFault.RepairedImage(dirtyImage, fault), dirtyCut + 1L);
            await GraphIncomingVertexAuthorityAssertions.Healthy(owner, cold, state, null, token);
            await GraphTraversalIntegrityAssertions.Replay(owner, cold, state.SeedCommand, state.SeedReceipt);
            receipt = GraphStorageReferenceFixture.Submit(owner, command).Get<CommitReceipt>();
            await GraphIncomingVertexAuthorityAssertions.Continuation(owner, command, receipt, next);
            await GraphIncomingVertexAuthorityAssertions.Healthy(owner, cold, state, next, token);
            await GraphTraversalIntegrityAssertions.Replay(owner, cold, command, receipt);
            finalImage = GraphShortestPathFullStoreImage.Bytes(cold);
            finalCut = cold.Position;
        }
        using var secondCold = new ZoneTreeStore(new(database.Directory), database.StorageExecution, UnitExecutionOptions.PointCacheExecution());
        var secondOwner = database.CreateConfiguredDatabase(secondCold);
        await GraphTraversalIntegrityAssertions.Image(secondCold, finalImage, finalCut);
        await GraphIncomingVertexAuthorityAssertions.Healthy(secondOwner, secondCold, state, next, token);
        await GraphIncomingVertexAuthorityAssertions.Continuation(secondOwner, command, receipt, next);
        await GraphTraversalIntegrityAssertions.Replay(secondOwner, secondCold, state.SeedCommand, state.SeedReceipt);
        await GraphTraversalIntegrityAssertions.Replay(secondOwner, secondCold, command, receipt);
    }

    private static GraphTraversalIntegrityState Seed(TestDatabase database)
    {
        database.Configure(GraphTraversalIntegrityFixture.Nodes, ResourceKind.Collection);
        database.Configure(GraphTraversalIntegrityFixture.Graph, ResourceKind.Graph);
        var healthySource = new EntityRef(database.Partition, GraphTraversalIntegrityFixture.Nodes, "incoming-healthy-source");
        var selectedSource = new EntityRef(database.Partition, GraphTraversalIntegrityFixture.Nodes, "incoming-selected-source");
        var target = new EntityRef(database.Partition, GraphTraversalIntegrityFixture.Nodes, "incoming-common-target");
        var healthy = new EdgeRecord("a-incoming-healthy", healthySource, target, "walk", "{}", 1L);
        var selected = new EdgeRecord("z-incoming-selected", selectedSource, target, "walk", "{}", 1L);
        var command = GraphStorageReferenceFixture.Command(database.Partition,
            new PutDocument(GraphTraversalIntegrityFixture.Nodes, healthySource.Id, "{}"),
            new PutDocument(GraphTraversalIntegrityFixture.Nodes, selectedSource.Id, "{}"),
            new PutDocument(GraphTraversalIntegrityFixture.Nodes, target.Id, "{}"),
            GraphTraversalIntegrityFixture.Mutation(healthy), GraphTraversalIntegrityFixture.Mutation(selected));
        return new(command, GraphStorageReferenceFixture.Submit(database.Database, command).Get<CommitReceipt>(), healthy, selected);
    }
}
