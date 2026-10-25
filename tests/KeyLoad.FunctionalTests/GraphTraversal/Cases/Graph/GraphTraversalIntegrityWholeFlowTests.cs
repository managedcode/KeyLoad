using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.GraphTraversal;

internal sealed class GraphTraversalIntegrityWholeFlowTests
{
    [Test]
    [Arguments(GraphTraversalIntegrityFault.MissingEdge, false)]
    [Arguments(GraphTraversalIntegrityFault.MissingEdge, true)]
    [Arguments(GraphTraversalIntegrityFault.EdgeIdentity, false)]
    [Arguments(GraphTraversalIntegrityFault.EdgeIdentity, true)]
    [Arguments(GraphTraversalIntegrityFault.EdgeSource, false)]
    [Arguments(GraphTraversalIntegrityFault.EdgeSource, true)]
    [Arguments(GraphTraversalIntegrityFault.AdjacencyIdentity, false)]
    [Arguments(GraphTraversalIntegrityFault.AdjacencyIdentity, true)]
    [Arguments(GraphTraversalIntegrityFault.InvalidLabel, false)]
    [Arguments(GraphTraversalIntegrityFault.InvalidLabel, true)]
    [Arguments(GraphTraversalIntegrityFault.InvalidTargetId, false)]
    [Arguments(GraphTraversalIntegrityFault.InvalidTargetId, true)]
    [Arguments(GraphTraversalIntegrityFault.InvalidAttributes, false)]
    [Arguments(GraphTraversalIntegrityFault.InvalidAttributes, true)]
    [Arguments(GraphTraversalIntegrityFault.NonpositiveRevision, false)]
    [Arguments(GraphTraversalIntegrityFault.NonpositiveRevision, true)]
    [Arguments(GraphTraversalIntegrityFault.NullTarget, false)]
    [Arguments(GraphTraversalIntegrityFault.NullTarget, true)]
    [Arguments(GraphTraversalIntegrityFault.MalformedAdjacency, false)]
    [Arguments(GraphTraversalIntegrityFault.MalformedAdjacency, true)]
    [Arguments(GraphTraversalIntegrityFault.WrongAdjacencyType, false)]
    [Arguments(GraphTraversalIntegrityFault.WrongAdjacencyType, true)]
    public async Task AcGraphAdjacency001002RefuseBeforeFilteringColdRepairReplayAndContinue(
        GraphTraversalIntegrityFault fault, bool filtered)
    {
        using var database = new TestDatabase();
        var state = GraphTraversalIntegrityFixture.Seed(database);
        var token = TestContext.Current!.Execution.CancellationToken;
        await GraphTraversalIntegrityAssertions.Healthy(database.Database, database.Store, state, null, filtered, token);
        var healthyImage = CanonicalStoreImage.Bytes(database.Store);
        var ownedFault = GraphTraversalIntegrityFaultRows.Install(database.Store, state, fault);
        await GraphTraversalIntegrityAssertions.Refuse(database.Database, database.Store, state, filtered, token);
        await GraphTraversalIntegrityAssertions.Refuse(database.Database, database.Store, state, filtered, token);
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
            await GraphTraversalIntegrityAssertions.Refuse(owner, cold, state, filtered, token);
            GraphTraversalIntegrityFaultRows.Repair(cold, ownedFault);
            await GraphTraversalIntegrityAssertions.Image(cold, healthyImage, corruptPosition + 1);
            await GraphTraversalIntegrityAssertions.Healthy(owner, cold, state, null, filtered, token);
            await GraphTraversalIntegrityAssertions.Replay(owner, cold, state.SeedCommand, state.SeedReceipt);
            continuation = GraphTraversalIntegrityFixture.Continue(owner, state);
            await GraphTraversalIntegrityAssertions.Healthy(owner, cold, state, continuation, filtered, token);
            await GraphTraversalIntegrityAssertions.Replay(owner, cold, continuation.Command, continuation.Receipt);
            continuedImage = CanonicalStoreImage.Bytes(cold);
            continuedPosition = cold.Position;
        }
        using var secondCold = new ZoneTreeStore(new(database.Directory), database.StorageExecution, UnitExecutionOptions.PointCacheExecution());
        var secondOwner = database.CreateConfiguredDatabase(secondCold);
        await GraphTraversalIntegrityAssertions.Image(secondCold, continuedImage, continuedPosition);
        await GraphTraversalIntegrityAssertions.Healthy(secondOwner, secondCold, state, continuation, filtered, token);
        await GraphTraversalIntegrityAssertions.Replay(secondOwner, secondCold, state.SeedCommand, state.SeedReceipt);
        await GraphTraversalIntegrityAssertions.Replay(secondOwner, secondCold, continuation.Command, continuation.Receipt);
    }
}
