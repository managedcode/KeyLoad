using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class PackedAnnNeighborCountTests
{
    private const int RecordCount = 128;
    private const int Dimension = 8;
    private const ulong Seed = PackedAnnTestData.CorpusSeed;

    [Test]
    public async Task AcAnnNeighborCountsPreserveRealGraphAndExactCandidatesForEveryMetric()
    {
        foreach (var metric in new[] { DistanceMetric.Cosine, DistanceMetric.Euclidean, DistanceMetric.DotProduct })
        {
            await AssertMetricAsync(metric);
        }
    }

    private static async Task AssertMetricAsync(DistanceMetric metric)
    {
        using var database = new TestDatabase();
        PackedAnnTestData.Configure(database);
        PackedAnnTestData.Seed(database, RecordCount, Dimension, metric);
        var records = PackedAnnTestData.Load(database, metric);
        var space = PackedAnnTestData.Space(metric, Dimension);
        var options = Options();
        var measuredBudget = PackedAnnIndexTestSupport.Budget(database);
        var measured = PackedAnnBuilder.Build(space, records, options, measuredBudget);
        var measuredGraph = PackedAnnAdjacencyAssertions.Snapshot(measured, PackedAnnIndexTestSupport.Budget(database));
        var slots = await AssertShapeAsync(measured, measuredGraph);
        await AssertReplaceClearRestoreAsync(measured, measuredGraph, slots, database);
        await AssertCountBudgetAndCancellationAsync(measured,
            (slots.PartialUpperSource, slots.PartialUpperLayer), database);
        var replay = PackedAnnBuilder.Build(space, records, options,
            PackedAnnIndexTestSupport.Budget(database, measuredBudget.WorkUnits));
        await PackedAnnAdjacencyAssertions.AssertSameAsync(measuredGraph,
            PackedAnnAdjacencyAssertions.Snapshot(replay, PackedAnnIndexTestSupport.Budget(database)));
        await AssertOneUnderBuildLimitAsync(space, records, options, measuredBudget.WorkUnits, database);
        await AssertCandidatesMatchPublicExactAsync(space, records, options, metric, database);
    }

    private static PackedAnnOptions Options() => new()
    {
        Connections = 4,
        EfConstruction = 8,
        EfSearch = RecordCount,
        ExactThreshold = 0,
        MaxRecords = RecordCount,
        Seed = Seed
    };

    private static async Task<(int FullBaseSource, int FullBaseLayer, int PartialUpperSource, int PartialUpperLayer)>
        AssertShapeAsync(PackedAnnState state, int[][][] adjacency)
    {
        var fullBase = 0;
        var partialBase = 0;
        var fullUpper = 0;
        var partialUpper = 0;
        for (var source = 0; source < state.Count; source++)
        {
            for (var layer = 0; layer <= state.Levels[source]; layer++)
            {
                var neighbors = adjacency[source][layer];
                var degree = state.Graph.Degree(layer);
                await Assert.That(neighbors.Length).IsLessThanOrEqualTo(degree);
                if (layer == 0 && neighbors.Length == degree)
                { fullBase++; }
                if (layer == 0 && neighbors.Length is > 0 && neighbors.Length < degree)
                { partialBase++; }
                if (layer > 0 && neighbors.Length == degree)
                { fullUpper++; }
                if (layer > 0 && neighbors.Length is > 0 && neighbors.Length < degree)
                { partialUpper++; }
                var seen = new HashSet<int>();
                foreach (var neighbor in neighbors)
                {
                    await Assert.That(neighbor).IsNotEqualTo(source);
                    await Assert.That((uint)neighbor < (uint)state.Count).IsTrue();
                    await Assert.That((int)state.Levels[neighbor]).IsGreaterThanOrEqualTo(layer);
                    await Assert.That(seen.Add(neighbor)).IsTrue();
                }
            }
        }
        await Assert.That(partialBase).IsGreaterThan(0);
        await Assert.That(fullBase).IsGreaterThan(0);
        await Assert.That(partialUpper).IsGreaterThan(0);
        await Assert.That(fullUpper).IsGreaterThan(0);
        var fullBaseSlot = Find(state, adjacency, layer: 0, requireFull: true);
        var partialUpperSlot = Find(state, adjacency, layer: 1, requireFull: false);
        return (fullBaseSlot.Source, fullBaseSlot.Layer, partialUpperSlot.Source, partialUpperSlot.Layer);
    }

    private static async Task AssertReplaceClearRestoreAsync(PackedAnnState state, int[][][] snapshot,
        (int FullBaseSource, int FullBaseLayer, int PartialUpperSource, int PartialUpperLayer) slots,
        TestDatabase database)
    {
        await ReplaceAndRestoreAsync(state.Graph, slots.FullBaseSource, slots.FullBaseLayer,
            snapshot[slots.FullBaseSource][slots.FullBaseLayer], database);
        await ReplaceAndRestoreAsync(state.Graph, slots.PartialUpperSource, slots.PartialUpperLayer,
            snapshot[slots.PartialUpperSource][slots.PartialUpperLayer], database);
    }

    private static async Task ReplaceAndRestoreAsync(PackedAnnGraph graph, int source, int layer,
        int[] original, TestDatabase database)
    {
        var budget = PackedAnnIndexTestSupport.Budget(database);
        var shortened = original[..^1];
        graph.ReplaceNeighbors(source, layer, shortened, budget);
        await Assert.That(graph.NeighborCount(source, layer, budget)).IsEqualTo(shortened.Length);
        for (var index = 0; index < shortened.Length; index++)
        { await Assert.That(graph.Neighbor(source, layer, index)).IsEqualTo(shortened[index]); }
        graph.ReplaceNeighbors(source, layer, ReadOnlySpan<int>.Empty, budget);
        await Assert.That(graph.NeighborCount(source, layer, budget)).IsZero();
        await Assert.That(graph.Neighbor(source, layer, 0)).IsEqualTo(-1);
        graph.ReplaceNeighbors(source, layer, original, budget);
        await Assert.That(graph.NeighborCount(source, layer, budget)).IsEqualTo(original.Length);
        for (var index = 0; index < original.Length; index++)
        { await Assert.That(graph.Neighbor(source, layer, index)).IsEqualTo(original[index]); }
    }

    private static async Task AssertCountBudgetAndCancellationAsync(PackedAnnState state,
        (int Source, int Layer) slot, TestDatabase database)
    {
        var measured = PackedAnnIndexTestSupport.Budget(database);
        measured.Charge(1);
        var expected = state.Graph.NeighborCount(slot.Source, slot.Layer, measured);
        var work = measured.WorkUnits;
        var exact = PackedAnnIndexTestSupport.Budget(database, work);
        exact.Charge(1);
        await Assert.That(state.Graph.NeighborCount(slot.Source, slot.Layer, exact)).IsEqualTo(expected);
        await Assert.That(exact.WorkUnits).IsEqualTo(work);
        var below = PackedAnnIndexTestSupport.Budget(database, work - 1);
        below.Charge(1);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            state.Graph.NeighborCount(slot.Source, slot.Layer, below));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var canceled = new AnnWorkBudget(new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits),
            TimeProvider.System, cancellation.Token), PackedAnnIndexTestSupport.GenerousWorkLimit);
        Assert.ThrowsExactly<OperationCanceledException>(() => state.Graph.NeighborCount(slot.Source, slot.Layer, canceled));
    }

    private static async Task AssertOneUnderBuildLimitAsync(VectorSpace space, VectorRecord[] records,
        PackedAnnOptions options, long work, TestDatabase database)
    {
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => PackedAnnBuilder.Build(space, records,
            options, PackedAnnIndexTestSupport.Budget(database, work - 1)));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    private static async Task AssertCandidatesMatchPublicExactAsync(VectorSpace space, VectorRecord[] records,
        PackedAnnOptions options, DistanceMetric metric, TestDatabase database)
    {
        var index = PackedAnnIndex.Build(space, records, UnitExecutionOptions.PackedAnn(options), PackedAnnIndexTestSupport.Budget(database));
        var query = PackedAnnTestData.Vector(1_001, Dimension, Seed);
        var actual = index.Search(query, records.Length, null, PackedAnnIndexTestSupport.Budget(database));
        var exact = records.Select((record, ordinal) => new AnnCandidate(ordinal, record.DocumentId,
                record.DocumentRevision, SearchEngine.Similarity(query, record.Values.ToArray(), metric)))
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.DocumentId, StringComparer.Ordinal).ToArray();
        await Assert.That(actual.Candidates.Length).IsEqualTo(exact.Length);
        for (var indexOrdinal = 0; indexOrdinal < exact.Length; indexOrdinal++)
        {
            await Assert.That(actual.Candidates[indexOrdinal].DocumentId).IsEqualTo(exact[indexOrdinal].DocumentId);
            await Assert.That(actual.Candidates[indexOrdinal].SourceOrdinal).IsEqualTo(exact[indexOrdinal].SourceOrdinal);
            await Assert.That(actual.Candidates[indexOrdinal].Score).IsEqualTo(exact[indexOrdinal].Score);
        }
    }

    private static (int Source, int Layer) Find(PackedAnnState state, int[][][] adjacency,
        int layer, bool requireFull)
    {
        for (var source = 0; source < state.Count; source++)
        {
            var maximumLayer = layer == 0 ? 0 : state.Levels[source];
            for (var candidateLayer = layer; candidateLayer <= maximumLayer; candidateLayer++)
            {
                var count = adjacency[source][candidateLayer].Length;
                if (MatchesDegree(count, state.Graph.Degree(candidateLayer), requireFull))
                { return (source, candidateLayer); }
            }
        }
        throw new InvalidOperationException("The seeded real ANN graph does not include the required adjacency shape.");
    }

    private static bool MatchesDegree(int count, int degree, bool requireFull) =>
        requireFull ? count == degree : count is > 0 && count < degree;

}
