using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.GraphTraversal;

internal sealed class GraphShortestPathCorruptionTests
{
    [Test]
    [Arguments(GraphShortestPathCorruptionCase.MissingEdge)]
    [Arguments(GraphShortestPathCorruptionCase.EdgeIdentity)]
    [Arguments(GraphShortestPathCorruptionCase.EdgeSource)]
    [Arguments(GraphShortestPathCorruptionCase.ForeignTarget)]
    [Arguments(GraphShortestPathCorruptionCase.InvalidLabel)]
    [Arguments(GraphShortestPathCorruptionCase.InvalidTargetId)]
    [Arguments(GraphShortestPathCorruptionCase.MalformedAdjacency)]
    [Arguments(GraphShortestPathCorruptionCase.WrongAdjacencyType)]
    public async Task AcGraph008PersistedCorruptionRefusesWithoutEffectsAcrossColdOpenThenExactRepairIsHealthy(
        GraphShortestPathCorruptionCase testCase)
    {
        using var database = GraphShortestPathTestSupport.CreateDatabase(null, GraphShortestPathCorruptionFlow.Nodes);
        var expected = GraphShortestPathCorruptionFlow.Seed(database);
        var request = GraphShortestPathCorruptionFlow.Request(expected);
        var token = TestContext.Current!.Execution.CancellationToken;
        await GraphShortestPathCorruptionFlow.HealthyAsync(database.Database, database.Store, expected, token);
        GraphShortestPathCorruptionFlow.Inject(database.Store, expected, testCase);
        await GraphShortestPathCorruptionFlow.RefuseAsync(database.Database, database.Store, request, token);
        var corruptImage = CanonicalStoreImage.Bytes(database.Store);
        var corruptPosition = database.Store.Position;
        database.Store.Dispose();

        using var reopened = new ZoneTreeStore(new(database.Directory), database.StorageExecution,
            UnitExecutionOptions.PointCacheExecution());
        var owner = database.CreateConfiguredDatabase(reopened);
        await Assert.That(reopened.Position).IsEqualTo(corruptPosition);
        await Assert.That(CanonicalStoreImage.Bytes(reopened).SequenceEqual(corruptImage)).IsTrue();
        await GraphShortestPathCorruptionFlow.RefuseAsync(owner, reopened, request, token);
        GraphShortestPathCorruptionFlow.Repair(reopened, expected, testCase);
        await GraphShortestPathCorruptionFlow.HealthyAsync(owner, reopened, expected, token);
    }
}
