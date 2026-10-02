using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class SearchTests
{
    private const string Orders = "orders";
    private const string Metrics = "metrics";
    private const string EmbeddingPath = "/embedding";
    private const string TextPath = "/text";
    private const string TestName = "test";
    private const string TestVersion = "1";
    private const string RootIdentity = "root";
    private const string DocumentA = "a";
    private const string DocumentB = "b";
    private const string LargeDocument = "large";
    private const string ClusterTerm = "cluster";
    private const string ChangedText = "\"changed\"";
    private const string SampleId = "sample";
    private const string LargeSeriesId = "large";
    private const string LargeVectorSpaceName = "large";
    private const int VectorDimensions = 2;
    private const int VectorVersion = 1;
    private const int FusionConstant = 61;
    private const double LargeFiniteSampleValue = 1e300;
    private const double TwelveDecimalPlacesTolerance = 0.0000000000005;
    private static VectorSpace Space { get; } = new(TestName, VectorDimensions, DistanceMetric.Cosine, TestName, TestVersion);

    [Test]
    public async Task HybridFusionRanksEligibleDocumentsAndInvalidatesStaleVectors()
    {
        using var database = new TestDatabase();
        database.Configure(Orders, ResourceKind.Collection);
        database.Commit(new PutDocument(Orders, DocumentA, "{\"text\":\"cluster database\"}"),
            new PutDocument(Orders, DocumentB, "{\"text\":\"database\"}"));
        database.Commit(
            new PutVector(Orders, DocumentA, EmbeddingPath, [1, 0], Space, VectorVersion),
            new PutVector(Orders, DocumentB, EmbeddingPath, [0, 1], Space, VectorVersion));
        var search = new SearchEngine(database.Database);
        var result = search.Search(RootIdentity, new(database.Partition, Orders, TextPath, ClusterTerm, EmbeddingPath, [1, 0], Space),
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result[0].Document.Reference.Id).IsEqualTo(DocumentA);
        await Assert.That(result[0].Score).IsEqualTo(2.0 / FusionConstant).Within(TwelveDecimalPlacesTolerance);

        database.Commit(new PatchDocument(Orders, DocumentA, [new(TextPath, PatchKind.Set, ChangedText)], VectorVersion));
        var vectors = search.Search(RootIdentity, new(database.Partition, Orders, VectorField: EmbeddingPath, Vector: [1, 0], Space: Space),
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(System.Linq.Enumerable.Single(vectors).Document.Reference.Id).IsEqualTo(DocumentB);
    }

    [Test]
    public async Task LargeFiniteVectorsAndSampleValuesRemainValidAndSimilarityDoesNotOverflow()
    {
        using var database = new TestDatabase();
        database.Configure(Orders, ResourceKind.Collection);
        database.Configure(Metrics, ResourceKind.TimeSeries);
        var values = Enumerable.Repeat(float.MaxValue, System.Numerics.Vector<float>.Count + 1).ToArray();
        var space = new VectorSpace(LargeVectorSpaceName, values.Length, DistanceMetric.Cosine, TestName, TestVersion);
        database.Commit(
            new PutDocument(Orders, LargeDocument, "{}"),
            new PutVector(Orders, LargeDocument, EmbeddingPath, [.. values], space, VectorVersion),
            new AppendSamples(Metrics, LargeSeriesId, [new(SampleId, TimeProvider.System.GetUtcNow(), LargeFiniteSampleValue)]));

        var match = await Assert.That(new SearchEngine(database.Database).Search(RootIdentity, new(database.Partition, Orders,
            VectorField: EmbeddingPath, Vector: [.. values], Space: space), TestContext.Current!.Execution.CancellationToken)).HasSingleItem();
        await Assert.That(match.Document.Reference.Id).IsEqualTo(LargeDocument);
        await Assert.That(SearchEngine.Similarity(values, values, DistanceMetric.Cosine)).IsEqualTo(1).Within(TwelveDecimalPlacesTolerance);
        await Assert.That(double.IsFinite(SearchEngine.Similarity(values, values, DistanceMetric.DotProduct))).IsTrue();
        await Assert.That(SearchEngine.Similarity(values, values, DistanceMetric.Euclidean)).IsEqualTo(0);
    }
}
