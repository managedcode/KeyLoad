using KeyLoad.Query;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class HybridExplainPrivacyWholeFlowTests
{
    private const string Reader = "explain-restricted-reader";
    private const string Owned = "owned";
    private const string Hidden = "private-hidden-canary";
    private const string Alice = "alice";
    private const string Bob = "bob";
    private const string TextRead = "filtered.search.text.read";
    private const string VectorRead = "filtered.search.vector.read";
    private const string Json = "{\"text\":\"alpha\"}";
    private const int FirstRank = 1;
    private const int Weight = 1;
    private const int Denominator = 14;
    private const int Branches = 2;

    [Test]
    public async Task ActualOrdinaryHybridExplainNeverDisclosesDeniedRowAndPreservesCanonicalState()
    {
        using var db = FilteredSearchTestSupport.Create();
        db.Commit(new PutDocument(FilteredSearchTestSupport.Collection, Owned, Json, Access: new(Alice)),
            new PutDocument(FilteredSearchTestSupport.Collection, Hidden, Json, Access: new(Bob)),
            new PutVector(FilteredSearchTestSupport.Collection, Owned, FilteredSearchTestSupport.VectorField,
                [1, 0], FilteredSearchTestSupport.Space, FilteredSearchTestSupport.Revision),
            new PutVector(FilteredSearchTestSupport.Collection, Hidden, FilteredSearchTestSupport.VectorField,
                [1, 0], FilteredSearchTestSupport.Space, FilteredSearchTestSupport.Revision));
        FilteredSearchTestSupport.PersistReader(db, Reader, Capability.VectorSearch,
            [TextRead, VectorRead, FilteredSearchTestSupport.TextUseGrant, FilteredSearchTestSupport.VectorUseGrant],
            Alice, restrictRows: true);
        var before = HybridExplainWholeFlow.Snapshot(db);
        var position = db.Store.Position;
        var request = FilteredSearchTestSupport.Request([Owned, Hidden]) with { Explain = true };
        var engine = new SearchEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var actual = await engine.SearchAsync(Reader, request, TestContext.Current!.Execution.CancellationToken);
        RankedDocument[] expected =
        [
            new(new(new(db.Partition, FilteredSearchTestSupport.Collection, Owned),
                FilteredSearchTestSupport.Revision, Json, false, []), Branches / (double)Denominator,
                new(FilteredSearchTestSupport.FusionConstant,
                [new(SearchBranchKind.Text, FirstRank, Weight, Weight / (double)Denominator),
                    new(SearchBranchKind.Vector, FirstRank, Weight, Weight / (double)Denominator)]))
        ];
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        var plain = await engine.SearchAsync(Reader, request with { Explain = false },
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(JsonDefaults.Serialize(plain).AsSpan().SequenceEqual(JsonDefaults.Serialize(
            expected.Select(hit => hit with { Explanation = null }).ToArray()))).IsTrue();
        await Assert.That(HybridExplainWholeFlow.Snapshot(db)).IsEquivalentTo(before, CollectionOrdering.Matching);
        await Assert.That(db.Store.Position).IsEqualTo(position);
    }
}
