using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal sealed class GraphShortestPathIntegrityWholeFlowTests
{
    [Test]
    [Arguments(GraphTraversalIntegrityFault.InvalidAttributes, false)]
    [Arguments(GraphTraversalIntegrityFault.InvalidAttributes, true)]
    [Arguments(GraphTraversalIntegrityFault.NonpositiveRevision, false)]
    [Arguments(GraphTraversalIntegrityFault.NonpositiveRevision, true)]
    public async Task AcGraphPathIntegrity001RefuseBeforeFilteringColdRepairReplayAndContinue(
        GraphTraversalIntegrityFault fault, bool filtered)
    {
        using var database = new TestDatabase();
        var state = GraphTraversalIntegrityFixture.Seed(database);
        var token = TestContext.Current!.Execution.CancellationToken;
        await GraphShortestPathIntegrityAssertions.Healthy(database.Database, database.Store, state, null, filtered, token);
        var healthyImage = GraphShortestPathFullStoreImage.Bytes(database.Store);
        var ownedFault = GraphTraversalIntegrityFaultRows.Install(database.Store, state, fault);
        await GraphShortestPathIntegrityAssertions.Refuse(database.Database, database.Store, state, filtered, token);
        await GraphShortestPathIntegrityAssertions.Refuse(database.Database, database.Store, state, filtered, token);
        var corruptImage = GraphShortestPathFullStoreImage.Bytes(database.Store);
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
            continuedImage = GraphShortestPathFullStoreImage.Bytes(cold);
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
