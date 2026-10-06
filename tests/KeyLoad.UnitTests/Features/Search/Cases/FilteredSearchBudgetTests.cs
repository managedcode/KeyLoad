using System.Collections.Immutable;
using KeyLoad.Query;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class FilteredSearchBudgetTests
{
    private const string Root = "root";

    [Test]
    public async Task AllowlistShapeAndCountFailBeforeStorageReads()
    {
        using var database = FilteredSearchTestSupport.Create(new() { MaxScanRecords = 1 });
        FilteredSearchTestSupport.AddCorpus(database);
        var engine = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var position = database.Store.Position;
        var malformed = (await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            engine.SearchAsync("absent-principal", VectorRequest(default), Token())))!;
        var control = await Failure(engine, VectorRequest(ImmutableArray.Create("\u0001bad")));
        var tooMany = await Failure(engine, VectorRequest(ImmutableArray.Create("a", "b")));

        await Assert.That(malformed.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(control.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(tooMany.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(database.Store.Position).IsEqualTo(position);
    }

    [Test]
    public async Task SerializedRequestByteLimitIsInclusiveAndExcessIsTyped()
    {
        var request = VectorRequest(ImmutableArray.Create("a"));
        var serializedBytes = JsonDefaults.Serialize(request).Length;
        using var exact = FilteredSearchTestSupport.Create(new() { MaxQueryBytes = serializedBytes });
        FilteredSearchTestSupport.AddCorpus(exact);
        var accepted = await new SearchEngine(exact.Database, UnitExecutionOptions.QueryExecution()).SearchAsync(Root, request, Token());
        await Assert.That(accepted.Select(result => result.Document.Reference.Id).ToArray())
            .IsEquivalentTo(new[] { "a" }, CollectionOrdering.Matching);

        using var excess = FilteredSearchTestSupport.Create(new() { MaxQueryBytes = serializedBytes - 1 });
        FilteredSearchTestSupport.AddCorpus(excess);
        var failure = await Failure(new SearchEngine(excess.Database, UnitExecutionOptions.QueryExecution()), request);
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    [Test]
    public async Task PreCanceledSearchReturnsNoPartialResultAndFollowingQuerySucceeds()
    {
        using var database = FilteredSearchTestSupport.Create();
        FilteredSearchTestSupport.AddCorpus(database);
        var engine = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution());
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            engine.SearchAsync(Root, VectorRequest(ImmutableArray.Create("a")), canceled.Token));
        var following = await engine.SearchAsync(Root, VectorRequest(ImmutableArray.Create("a")), Token());

        await Assert.That(following).HasSingleItem();
        await Assert.That(following[0].Document.Reference.Id).IsEqualTo("a");
    }

    private static SearchRequest VectorRequest(ImmutableArray<string> allowedIds)
        => new(new("tenant", "database", "orders", FilteredSearchTestSupport.PartitionKey), FilteredSearchTestSupport.Collection,
            VectorField: FilteredSearchTestSupport.VectorField, Vector: [1, 0],
            Space: FilteredSearchTestSupport.Space, AllowedIds: allowedIds);

    private static async Task<KeyLoadException> Failure(SearchEngine engine, SearchRequest request)
        => (await Assert.ThrowsExactlyAsync<KeyLoadException>(() => engine.SearchAsync(Root, request, Token())))!;

    private static CancellationToken Token() => TestContext.Current!.Execution.CancellationToken;
}
