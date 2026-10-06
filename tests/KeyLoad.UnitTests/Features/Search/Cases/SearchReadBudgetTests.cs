using KeyLoad.Core;
using KeyLoad.Query;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class SearchReadBudgetTests
{
    private const string Orders = "orders";
    private const string EmbeddingPath = "/embedding";
    private const string TextPath = "/text";
    private const string TestName = "test";
    private const string TestVersion = "1";
    private const string DocumentKeySpace = "document";
    private const string VectorKeySpace = "vector";
    private const string LineageKeySpace = "vector-projection-lineage";
    private const int VectorDimensions = 2;
    private const int VectorVersion = 1;
    private const int PaddingLength = 12_000;
    private const int ReadByteLimit = 4_096;
    private const int MaximumVisibleTextTokens = 5;
    private const int TwoDocuments = 2;
    private const int MaximumScanRecords = 100;
    private const int FusionConstant = 61;
    private const double TwelveDecimalPlacesTolerance = 0.0000000000005;
    private static VectorSpace Space { get; } = new(TestName, VectorDimensions, DistanceMetric.Cosine, TestName, TestVersion);

    [Test]
    public async Task VectorDocumentDereferencesConsumeTheReadByteBudget()
    {
        using var database = new TestDatabase(new() { MaxQueryReadBytes = ReadByteLimit });
        database.Configure(Orders, ResourceKind.Collection);
        database.Commit(new PutDocument(Orders, "a", "{\"padding\":\"" + new string('x', PaddingLength) + "\"}"),
            new PutVector(Orders, "a", EmbeddingPath, [1, 0], Space, VectorVersion));
        var position = database.Store.Position;
        var failure = (await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution()).SearchAsync("root", VectorRequest(database), TestContext.Current!.Execution.CancellationToken)))!;
        await Assert.That(failure.Code)
            .IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(database.Store.Position).IsEqualTo(position);
    }

    [Test]
    public async Task TextAndVectorBranchesShareOneReadByteBudget()
    {
        using var database = new TestDatabase();
        database.Configure(Orders, ResourceKind.Collection);
        database.Commit(new PutDocument(Orders, "a", "{\"text\":\"alpha alpha\"}"),
            new PutVector(Orders, "a", EmbeddingPath, [1, 0], Space, VectorVersion));
        var documentBytes = Bytes(DocumentKeySpace, Orders);
        var vectorBytes = Bytes(VectorKeySpace, Orders, EmbeddingPath);
        var lineageBytes = KeySpace.Partition(LineageKeySpace, database.Partition, Orders, EmbeddingPath, "a").LongLength;
        var bounded = new DatabaseEngine(database.Store, database.Database.Authorization, UnitExecutionOptions.DatabaseLimits(new() { MaxQueryReadBytes = TwoDocuments * documentBytes + vectorBytes + lineageBytes }), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.TimeSeriesExecution());
        var search = new SearchEngine(bounded, UnitExecutionOptions.QueryExecution());
        var token = TestContext.Current!.Execution.CancellationToken;

        await Assert.That(await search.SearchAsync("root", new(database.Partition, Orders, TextPath, "alpha"), token)).HasSingleItem();
        await Assert.That(await search.SearchAsync("root", VectorRequest(database), token)).HasSingleItem();
        var failure = (await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            search.SearchAsync("root", new(database.Partition, Orders, TextPath, "alpha", EmbeddingPath, [1, 0], Space), token)))!;
        await Assert.That(failure.Code)
            .IsEqualTo(ErrorCode.BudgetExceeded);

        long Bytes(string space, params object?[] suffix) => database.Store.Read(view => view.Scan(
            KeySpace.Partition(space, database.Partition, suffix), MaximumScanRecords).Records.Sum(record =>
            (long)record.Key.Length + record.Value.Length));
    }

    [Test]
    public async Task TextTokenLimitAppliesToTheWholeVisibleCorpus()
    {
        using var database = new TestDatabase(new() { MaxSearchTextTokens = MaximumVisibleTextTokens });
        database.Configure(Orders, ResourceKind.Collection);
        database.Commit(new PutDocument(Orders, "a", "{\"text\":\"alpha beta gamma\"}"),
            new PutDocument(Orders, "b", "{\"text\":\"alpha beta gamma\"}"));
        var failure = (await Assert.ThrowsExactlyAsync<KeyLoadException>(() => new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution())
            .SearchAsync("root", new(database.Partition, Orders, TextPath, "alpha"), TestContext.Current!.Execution.CancellationToken)))!;
        await Assert.That(failure.Code)
            .IsEqualTo(ErrorCode.BudgetExceeded);
    }

    [Test]
    public async Task StreamingBm25PreservesUnicodeRepeatedTermsAndMissingFieldRanks()
    {
        using var database = new TestDatabase();
        database.Configure(Orders, ResourceKind.Collection);
        database.Commit(
            new PutDocument(Orders, "a", "{\"text\":\"CAFÉ café beta\"}"),
            new PutDocument(Orders, "b", "{\"text\":\"café gamma\"}"),
            new PutDocument(Orders, "c", "{\"text\":\"delta beta beta beta\"}"),
            new PutDocument(Orders, "d", "{}"));
        var result = await new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution()).SearchAsync("root",
            new(database.Partition, Orders, TextPath, "ＣＡＦÉ beta"), TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.Select(row => row.Document.Reference.Id)).IsEquivalentTo(new[] { "a", "c", "b" }, CollectionOrdering.Matching);
        for (var index = 0; index < result.Length; index++)
        {
            await Assert.That(result[index].Score).IsEqualTo(1.0 / (FusionConstant + index)).Within(TwelveDecimalPlacesTolerance);
        }
    }

    [Test]
    public async Task LargeFusionConstantAndWeightsKeepFinitePositiveScores()
    {
        using var database = new TestDatabase();
        database.Configure(Orders, ResourceKind.Collection);
        database.Commit(new PutDocument(Orders, "a", "{\"text\":\"alpha\"}"),
            new PutVector(Orders, "a", EmbeddingPath, [1, 0], Space, VectorVersion));
        var result = await Assert.That(await new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution()).SearchAsync("root",
            new(database.Partition, Orders, TextPath, "alpha", EmbeddingPath, [1, 0], Space,
                TextWeight: double.MaxValue, VectorWeight: double.MaxValue, FusionConstant: int.MaxValue),
            TestContext.Current!.Execution.CancellationToken)).HasSingleItem();
        await Assert.That(double.IsFinite(result.Score)).IsTrue();
        await Assert.That(result.Score > 0).IsTrue();
        var contribution = double.MaxValue / (int.MaxValue + 1.0);
        await Assert.That(result.Score).IsEqualTo(contribution + contribution);
    }

    private static SearchRequest VectorRequest(TestDatabase database) => new(database.Partition, Orders,
        VectorField: EmbeddingPath, Vector: [1, 0], Space: Space);
}
