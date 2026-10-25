using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.GraphTraversal;

internal sealed class GraphTraversalForeignFrontierWholeFlowTests
{
    [Test]
    public async Task AcGraphAdjacency003ForeignEntityKeepsFullIdentityWithoutExpandingLocalTextIdAcrossTwoColdOwners()
    {
        using var database = new TestDatabase();
        database.Configure(GraphCrossPartitionTestSupport.Collection, ResourceKind.Collection);
        database.Configure(GraphCrossPartitionTestSupport.Graph, ResourceKind.Graph);
        var state = GraphTraversalForeignFrontierFixture.Seed(database);
        var token = TestContext.Current!.Execution.CancellationToken;
        await GraphTraversalForeignFrontierAssertions.Healthy(database.Database, database.Store, state, null, token);
        var image = CanonicalStoreImage.Bytes(database.Store);
        var position = database.Store.Position;
        database.Store.Dispose();
        GraphTraversalIntegrityContinuation continuation;
        using (var cold = new ZoneTreeStore(new(database.Directory), database.StorageExecution, UnitExecutionOptions.PointCacheExecution()))
        {
            var owner = database.CreateConfiguredDatabase(cold);
            await GraphTraversalIntegrityAssertions.Image(cold, image, position);
            await GraphTraversalForeignFrontierAssertions.Healthy(owner, cold, state, null, token);
            continuation = GraphTraversalForeignFrontierFixture.Continue(owner, state);
            await GraphTraversalForeignFrontierAssertions.Healthy(owner, cold, state, continuation, token);
            image = CanonicalStoreImage.Bytes(cold);
            position = cold.Position;
        }
        using var secondCold = new ZoneTreeStore(new(database.Directory), database.StorageExecution, UnitExecutionOptions.PointCacheExecution());
        var secondOwner = database.CreateConfiguredDatabase(secondCold);
        await GraphTraversalIntegrityAssertions.Image(secondCold, image, position);
        await GraphTraversalForeignFrontierAssertions.Healthy(secondOwner, secondCold, state, continuation, token);
    }
}
