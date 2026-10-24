using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.GraphTraversal;

internal static class GraphShortestPathSelectedIdentityFlow
{
    internal static async Task Run(GraphTraversalIntegrityFault fault, bool filtered)
    {
        using var database = new TestDatabase();
        var state = GraphTraversalIntegrityFixture.Seed(database);
        var token = TestContext.Current!.Execution.CancellationToken;
        await GraphShortestPathIntegrityAssertions.Healthy(database.Database, database.Store, state, null, filtered, token);
        var healthyImage = CanonicalStoreImage.Bytes(database.Store);
        var healthyPosition = database.Store.Position;
        var ownedFault = GraphTraversalIntegrityFaultRows.Install(database.Store, state, fault);
        await Assert.That(database.Store.Position).IsEqualTo(healthyPosition + 1);
        await GraphShortestPathIntegrityAssertions.Refuse(database.Database, database.Store, state, filtered, token);
        await GraphShortestPathIntegrityAssertions.Refuse(database.Database, database.Store, state, filtered, token);
        var corruptImage = CanonicalStoreImage.Bytes(database.Store);
        var corruptPosition = database.Store.Position;
        database.Store.Dispose();
        GraphTraversalIntegrityContinuation continuation;
        string[] continuedImage;
        long continuedPosition;
        using (var cold = new ZoneTreeStore(new(database.Directory), database.StorageExecution, UnitExecutionOptions.PointCacheExecution()))
        {
            var owner = database.CreateConfiguredDatabase(cold);
            await GraphTraversalIntegrityAssertions.Image(cold, corruptImage, corruptPosition);
            await GraphShortestPathIntegrityAssertions.Refuse(owner, cold, state, filtered, token);
            GraphTraversalIntegrityFaultRows.Repair(cold, ownedFault);
            await GraphTraversalIntegrityAssertions.Image(cold, healthyImage, corruptPosition + 1);
            await GraphShortestPathIntegrityAssertions.Healthy(owner, cold, state, null, filtered, token);
            await GraphTraversalIntegrityAssertions.Replay(owner, cold, state.SeedCommand, state.SeedReceipt);
            continuation = GraphTraversalIntegrityFixture.Continue(owner, state);
            await GraphShortestPathIntegrityAssertions.Healthy(owner, cold, state, continuation, filtered, token);
            await GraphTraversalIntegrityAssertions.Replay(owner, cold, continuation.Command, continuation.Receipt);
            continuedImage = CanonicalStoreImage.Bytes(cold);
            continuedPosition = cold.Position;
        }
        using var secondCold = new ZoneTreeStore(new(database.Directory), database.StorageExecution, UnitExecutionOptions.PointCacheExecution());
        var secondOwner = database.CreateConfiguredDatabase(secondCold);
        await GraphTraversalIntegrityAssertions.Image(secondCold, continuedImage, continuedPosition);
        await GraphShortestPathIntegrityAssertions.Healthy(secondOwner, secondCold, state, continuation, filtered, token);
        await GraphTraversalIntegrityAssertions.Replay(secondOwner, secondCold, state.SeedCommand, state.SeedReceipt);
        await GraphTraversalIntegrityAssertions.Replay(secondOwner, secondCold, continuation.Command, continuation.Receipt);
    }
}
