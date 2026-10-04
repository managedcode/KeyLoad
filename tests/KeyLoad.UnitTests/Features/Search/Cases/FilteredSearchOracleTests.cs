using System.Collections.Immutable;
using KeyLoad.Query;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class FilteredSearchOracleTests
{
    private const string Root = "root";
    private const int Limit = 10;
    private const double TextWeight = 2;
    private const double VectorWeight = 3;

    [Test]
    public async Task AllowlistFiltersTextVectorAndHybridBeforeBranchRanksAndFusion()
    {
        using var database = FilteredSearchTestSupport.Create();
        FilteredSearchTestSupport.AddCorpus(database);
        var engine = new SearchEngine(database.Database);
        var allText = await engine.SearchAsync(Root, FilteredSearchTestSupport.Request(hybrid: false), Token());
        var allVector = await engine.SearchAsync(Root, VectorOnly(), Token());
        var allowed = ImmutableArray.Create("b", "a", "d", "b", "missing");
        var request = FilteredSearchTestSupport.Request(allowed) with
        {
            Limit = Limit,
            TextWeight = TextWeight,
            VectorWeight = VectorWeight
        };

        var actual = await engine.SearchAsync(Root, request, Token());
        var expected = ExpectedFusion(allText, allVector, allowed);

        await Assert.That(actual.Select(result => result.Document.Reference.Id).ToArray())
            .IsEquivalentTo(expected.Select(result => result.Id).ToArray(), CollectionOrdering.Matching);
        for (var index = 0; index < expected.Length; index++)
        {
            await Assert.That(actual[index].Document.Reference.Id).IsEqualTo(expected[index].Id);
            await Assert.That(actual[index].Score).IsEqualTo(expected[index].Score)
                .Within(FilteredSearchTestSupport.Tolerance);
        }

        var vectorOnly = await engine.SearchAsync(Root, VectorOnly() with { AllowedIds = allowed }, Token());
        await Assert.That(vectorOnly.Select(result => result.Document.Reference.Id).ToArray())
            .IsEquivalentTo(new[] { "a", "b", "d" }, CollectionOrdering.Matching);
        var empty = await engine.SearchAsync(Root,
            FilteredSearchTestSupport.Request(ImmutableArray<string>.Empty), Token());
        await Assert.That(empty).IsEmpty();
    }

    [Test]
    public async Task NullAllowlistPreservesTheExistingExactResultSequence()
    {
        using var database = FilteredSearchTestSupport.Create();
        FilteredSearchTestSupport.AddCorpus(database);
        var engine = new SearchEngine(database.Database);
        var legacy = await engine.SearchAsync(Root, FilteredSearchTestSupport.Request(), Token());
        var explicitNull = await engine.SearchAsync(Root,
            FilteredSearchTestSupport.Request(null), Token());

        await Assert.That(explicitNull).IsEquivalentTo(legacy, CollectionOrdering.Matching);
        for (var index = 0; index < legacy.Length; index++)
        {
            await Assert.That(explicitNull[index].Document.Reference.Id)
                .IsEqualTo(legacy[index].Document.Reference.Id);
            await Assert.That(explicitNull[index].Score).IsEqualTo(legacy[index].Score);
        }
    }

    private static SearchRequest VectorOnly() => new(new("tenant", "database", "orders", "customer-1"),
        FilteredSearchTestSupport.Collection, VectorField: FilteredSearchTestSupport.VectorField,
        Vector: [1, 0], Space: FilteredSearchTestSupport.Space, Limit: Limit,
        FusionConstant: FilteredSearchTestSupport.FusionConstant);

    private static (string Id, double Score)[] ExpectedFusion(RankedDocument[] text, RankedDocument[] vectors,
        ImmutableArray<string> allowed)
    {
        var membership = allowed.ToHashSet(StringComparer.Ordinal);
        var scores = new Dictionary<string, double>(StringComparer.Ordinal);
        Add(text, TextWeight);
        Add(vectors, VectorWeight);
        return scores.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => (pair.Key, pair.Value)).ToArray();

        void Add(RankedDocument[] branch, double weight)
        {
            var rank = 0;
            foreach (var result in branch)
            {
                var id = result.Document.Reference.Id;
                if (!membership.Contains(id))
                {
                    continue;
                }
                scores[id] = scores.GetValueOrDefault(id)
                    + weight / (FilteredSearchTestSupport.FusionConstant + rank + 1.0);
                rank++;
            }
        }
    }

    private static CancellationToken Token() => TestContext.Current!.Execution.CancellationToken;
}
