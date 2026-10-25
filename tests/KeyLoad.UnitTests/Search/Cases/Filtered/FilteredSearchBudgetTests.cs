using System.Collections.Immutable;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.Search;

internal sealed class FilteredSearchBudgetTests
{
    private const string Root = "root";

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

    private static CancellationToken Token() => TestContext.Current!.Execution.CancellationToken;
}
