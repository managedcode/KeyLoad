using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal static class GraphVertexAuthorityFlow
{
    internal const string Witness = "rolled-back-vertex";
    internal const string Payload = "{}";

    internal static async Task Run(bool start)
    {
        using var database = new TestDatabase();
        var state = GraphTraversalIntegrityFixture.Seed(database);
        var token = TestContext.Current!.Execution.CancellationToken;
        await GraphVertexAuthorityAssertions.Healthy(database.Database, database.Store, state, null, token);
        var fault = GraphVertexAuthorityFault.Install(database.Store, state, start);
        await GraphVertexAuthorityAssertions.ReadsRefuse(database.Database, database.Store, state, token);
        var upsert = GraphStorageReferenceFixture.Command(database.Partition,
            new PutDocument(GraphTraversalIntegrityFixture.Nodes, Witness, Payload),
            GraphTraversalIntegrityFixture.Mutation(state.DamagedEdge) with { ExpectedRevision = 1L });
        var remove = GraphStorageReferenceFixture.Command(database.Partition,
            new PutDocument(GraphTraversalIntegrityFixture.Nodes, Witness, Payload),
            new DeleteEdge(GraphTraversalIntegrityFixture.Graph, state.DamagedEdge.Id, 1L));
        var upsertFailure = await GraphVertexAuthorityAssertions.RefusedMutation(database.Database, database.Store, upsert);
        var removeFailure = await GraphVertexAuthorityAssertions.RefusedMutation(database.Database, database.Store, remove);
        var corruptImage = GraphShortestPathFullStoreImage.Bytes(database.Store);
        var corruptPosition = database.Store.Position;
        database.Store.Dispose();
        GraphTraversalIntegrityContinuation continuation;
        CommitReceipt recovered;
        string[] continuedImage;
        long continuedPosition;
        using (var cold = new ZoneTreeStore(new(database.Directory), database.StorageExecution, UnitExecutionOptions.PointCacheExecution()))
        {
            var owner = database.CreateConfiguredDatabase(cold);
            await GraphTraversalIntegrityAssertions.Image(cold, corruptImage, corruptPosition);
            await GraphVertexAuthorityAssertions.ReadsRefuse(owner, cold, state, token);
            await GraphVertexAuthorityAssertions.ReplayFailure(owner, cold, upsert, upsertFailure);
            await GraphVertexAuthorityAssertions.ReplayFailure(owner, cold, remove, removeFailure);
            GraphTraversalIntegrityFaultRows.Repair(cold, fault);
            await GraphTraversalIntegrityAssertions.Image(cold, GraphVertexAuthorityFault.RepairedImage(corruptImage, fault), corruptPosition + 1L);
            await GraphVertexAuthorityAssertions.Healthy(owner, cold, state, null, token);
            await GraphTraversalIntegrityAssertions.Replay(owner, cold, state.SeedCommand, state.SeedReceipt);
            recovered = GraphStorageReferenceFixture.Submit(owner, upsert).Get<CommitReceipt>();
            await GraphVertexAuthorityAssertions.RepairedReceipt(owner, state, upsert, recovered);
            state = state with { DamagedEdge = state.DamagedEdge with { Revision = 2L } };
            await GraphTraversalIntegrityAssertions.Replay(owner, cold, upsert, recovered);
            continuation = GraphTraversalIntegrityFixture.Continue(owner, state);
            await GraphVertexAuthorityAssertions.Healthy(owner, cold, state, continuation, token);
            await GraphTraversalIntegrityAssertions.Replay(owner, cold, continuation.Command, continuation.Receipt);
            continuedImage = GraphShortestPathFullStoreImage.Bytes(cold);
            continuedPosition = cold.Position;
        }
        using var secondCold = new ZoneTreeStore(new(database.Directory), database.StorageExecution, UnitExecutionOptions.PointCacheExecution());
        var secondOwner = database.CreateConfiguredDatabase(secondCold);
        await GraphTraversalIntegrityAssertions.Image(secondCold, continuedImage, continuedPosition);
        await GraphVertexAuthorityAssertions.Healthy(secondOwner, secondCold, state, continuation, token);
        await GraphVertexAuthorityAssertions.RepairedReceipt(secondOwner, state, upsert, recovered);
        await GraphTraversalIntegrityAssertions.Replay(secondOwner, secondCold, upsert, recovered);
        await GraphTraversalIntegrityAssertions.Replay(secondOwner, secondCold, state.SeedCommand, state.SeedReceipt);
        await GraphTraversalIntegrityAssertions.Replay(secondOwner, secondCold, continuation.Command, continuation.Receipt);
    }
}
