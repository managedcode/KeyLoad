using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class OpenSearchVectorQueryTests
{
    private const int WitnessDocuments = 4096, WitnessDimensions = 128, WitnessQuery = 170;
    private const int HigherNeighbor = 2289, LowerNeighbor = 1272;
    private const string Size = "size", Query = "query", Exists = "exists", Field = "field";
    private const string Aggregations = "aggregations", Exact = "exact_neighbors", Metric = "scripted_metric";
    private const string Parameters = "params", QueryValue = "query_value", TopK = "top_k";
    private const string Language = "lang", Painless = "painless", Source = "source";
    private const string FloatQueryCast = "(double)(float)params.query_value[i]";
    private const string NativeVector = "doc['vector'].value";
    private const string FloatScore = "script_score", ClientSort = "sort";
    private static readonly string[] Scripts = ["init_script", "map_script", "combine_script", "reduce_script"];

    /// <summary>AC-BC-FAIL-010: distinct canonical double scores collapse under translated float score ordering.</summary>
    [Test]
    public async Task PrecisionWitnessRequiresDoubleOrderingBeforeOrdinalTies()
    {
        var dataset = WitnessDataset();
        var query = dataset.Documents[WitnessQuery];
        var higher = IndependentCosine(query.Vector, dataset.Documents[HigherNeighbor].Vector);
        var lower = IndependentCosine(query.Vector, dataset.Documents[LowerNeighbor].Vector);
        await Assert.That(higher).IsGreaterThan(lower);
        await Assert.That((float)(1 + higher)).IsEqualTo((float)(1 + lower));
        await Assert.That(StringComparer.Ordinal.Compare(dataset.Documents[HigherNeighbor].Id, dataset.Documents[LowerNeighbor].Id)).IsGreaterThan(0);
        var oracle = dataset.ExactNeighbors(query);
        await Assert.That(oracle[4].Id).IsEqualTo(dataset.Documents[HigherNeighbor].Id);
        await Assert.That(oracle[5].Id).IsEqualTo(dataset.Documents[LowerNeighbor].Id);
    }

    /// <summary>AC-BC-FAIL-010: one native aggregation uses actual vector doc values, bounded TopK and float32 query semantics.</summary>
    [Test]
    public async Task QueryUsesNativeAggregationWithoutFloatScoreOrClientSort()
    {
        var dataset = WitnessDataset();
        using var json = JsonSerializer.SerializeToDocument(OpenSearchVectorQuery.Create(dataset.Documents[WitnessQuery].Vector, 10), OpenSearchHttp.JsonOptions);
        var root = json.RootElement;
        await Assert.That(root.GetProperty(Size).GetInt32()).IsEqualTo(0);
        await Assert.That(root.GetProperty(Query).GetProperty(Exists).GetProperty(Field).GetString()).IsEqualTo("vector");
        await Assert.That(root.GetProperty(Query).TryGetProperty(FloatScore, out _)).IsFalse();
        await Assert.That(root.TryGetProperty(ClientSort, out _)).IsFalse();
        var metric = root.GetProperty(Aggregations).GetProperty(Exact).GetProperty(Metric);
        await Assert.That(metric.GetProperty(Parameters).GetProperty(TopK).GetInt32()).IsEqualTo(10);
        await Assert.That(metric.GetProperty(Parameters).GetProperty(QueryValue).GetArrayLength()).IsEqualTo(WitnessDimensions);
        foreach (var script in Scripts)
        {
            await Assert.That(metric.GetProperty(script).GetProperty(Language).GetString()).IsEqualTo(Painless);
            await Assert.That(string.IsNullOrWhiteSpace(metric.GetProperty(script).GetProperty(Source).GetString())).IsFalse();
        }
        await Assert.That(metric.GetProperty(Scripts[0]).GetProperty(Source).GetString()!).Contains(FloatQueryCast);
        await Assert.That(metric.GetProperty(Scripts[1]).GetProperty(Source).GetString()!).Contains(NativeVector);
    }

    /// <summary>AC-BC-FAIL-010: the existing canonical native TopK and dimension budgets cannot be expanded by a query.</summary>
    [Test]
    [Arguments(0)]
    [Arguments(101)]
    public async Task QueryRejectsTopKOutsideCanonicalBounds(int topK)
        => await Assert.That(() => OpenSearchVectorQuery.Create([1, 0], topK)).Throws<ArgumentOutOfRangeException>();

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(1025)]
    public async Task QueryRejectsDimensionsOutsideCanonicalBounds(int dimensions)
        => await Assert.That(() => OpenSearchVectorQuery.Create(Enumerable.Repeat(1f, dimensions).ToImmutableArray(), 1)).Throws<ArgumentOutOfRangeException>();

    [Test]
    public async Task QueryRejectsNonFiniteVectorInputs()
    {
        await Assert.That(() => OpenSearchVectorQuery.Create([float.NaN, 1], 1)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => OpenSearchVectorQuery.Create([float.PositiveInfinity, 1], 1)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => OpenSearchVectorQuery.Create(default, 1)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    [Arguments(2, 1)]
    [Arguments(1024, 100)]
    public async Task QueryAcceptsCanonicalBoundaryDimensionsAndTopK(int dimensions, int topK)
    {
        using var json = JsonSerializer.SerializeToDocument(OpenSearchVectorQuery.Create(Enumerable.Repeat(1f, dimensions).ToImmutableArray(), topK),
            OpenSearchHttp.JsonOptions);
        var parameters = json.RootElement.GetProperty(Aggregations).GetProperty(Exact).GetProperty(Metric).GetProperty(Parameters);
        await Assert.That(parameters.GetProperty(QueryValue).GetArrayLength()).IsEqualTo(dimensions);
        await Assert.That(parameters.GetProperty(TopK).GetInt32()).IsEqualTo(topK);
    }

    internal static BenchmarkDataset WitnessDataset() => new(new ComparisonOptions
    {
        Seed = 1729,
        Documents = WitnessDocuments,
        Dimensions = WitnessDimensions,
        TopK = 10,
        Operations = 1,
        Warmup = 0,
        Repetitions = 1,
        Concurrency = 1
    });

    private static double IndependentCosine(ImmutableArray<float> query, ImmutableArray<float> candidate)
    {
        var querySquares = query.Select(value => (double)value * value).Sum();
        var candidateSquares = candidate.Select(value => (double)value * value).Sum();
        var dot = query.Zip(candidate, (left, right) => (double)left * right).Sum();
        return dot / Math.Sqrt(querySquares * candidateSquares);
    }
}
