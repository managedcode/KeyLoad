using KeyLoad.Query.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal static class PackedAnnReciprocalInsertionAssertions
{
    private const int Connections = 4;
    private const int Dimension = PackedAnnTestData.QualityDimension;

    internal static async Task AssertCorpusAsync(int recordCount, int efConstruction)
    {
        using var database = new TestDatabase();
        PackedAnnTestData.Configure(database);
        PackedAnnTestData.Seed(database, recordCount, Dimension, DistanceMetric.DotProduct);
        var records = PackedAnnTestData.Load(database, DistanceMetric.DotProduct);
        var space = PackedAnnTestData.Space(DistanceMetric.DotProduct, Dimension);
        var options = new PackedAnnOptions
        {
            Connections = Connections,
            EfConstruction = efConstruction,
            EfSearch = 1,
            ExactThreshold = 0,
            Seed = PackedAnnTestData.CorpusSeed
        };
        var budget = PackedAnnIndexTestSupport.Budget(database);
        var measured = PackedAnnReciprocalObservation.Build(space, records, options, budget);
        var state = measured.State;
        var adjacency = PackedAnnAdjacencyAssertions.Snapshot(state, PackedAnnIndexTestSupport.Budget(database));
        PackedAnnReciprocalObservation.Write(measured, adjacency, options, budget);
        await Assert.That(records.Length).IsEqualTo(recordCount);
        await AssertValidAdjacencyAsync(state, records, adjacency);
        var replayBudget = PackedAnnIndexTestSupport.Budget(database);
        var replayMeasured = PackedAnnReciprocalObservation.Build(space, records, options, replayBudget);
        var replay = replayMeasured.State;
        var replayAdjacency = PackedAnnAdjacencyAssertions.Snapshot(replay, PackedAnnIndexTestSupport.Budget(database));
        PackedAnnReciprocalObservation.Write(replayMeasured, replayAdjacency, options, replayBudget);
        await PackedAnnAdjacencyAssertions.AssertSameAsync(adjacency, replayAdjacency);
        await Assert.That(replayBudget.WorkUnits).IsEqualTo(budget.WorkUnits);
        await Assert.That(replayBudget.DistanceEvaluations).IsEqualTo(budget.DistanceEvaluations);
        await Assert.That(replayBudget.EdgeVisits).IsEqualTo(budget.EdgeVisits);
    }

    private static async Task AssertValidAdjacencyAsync(PackedAnnState state, VectorRecord[] records,
        int[][][] adjacency)
    {
        await Assert.That(state.Count).IsEqualTo(records.Length);
        await Assert.That(adjacency.Length).IsEqualTo(records.Length);
        for (var source = 0; source < adjacency.Length; source++)
        {
            await Assert.That(state.Ids[source]).IsEqualTo(records[source].DocumentId);
            await Assert.That(adjacency[source].Length).IsEqualTo(state.Levels[source] + 1);
            for (var layer = 0; layer < adjacency[source].Length; layer++)
            {
                var neighbors = adjacency[source][layer];
                await Assert.That(neighbors.Length <= state.Graph.Degree(layer)).IsTrue();
                await AssertOrderedNeighborsAsync(state, records, source, layer, neighbors);
            }
        }
    }

    private static async Task AssertOrderedNeighborsAsync(PackedAnnState state, VectorRecord[] records,
        int source, int layer, int[] neighbors)
    {
        var previous = -1;
        foreach (var neighbor in neighbors)
        {
            var valid = (uint)neighbor < (uint)state.Count && neighbor != source
                && state.Levels[neighbor] >= layer && neighbor > previous
                && state.Ids[neighbor] == records[neighbor].DocumentId;
            await Assert.That(valid).IsTrue();
            previous = neighbor;
        }
    }
}
