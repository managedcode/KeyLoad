using TUnit.Assertions.Enums;
using static KeyLoad.UnitTests.Features.Search.ThreeWayHybridTestSupport;

namespace KeyLoad.UnitTests.Features.Search;

internal static class HybridExplainWholeFlow
{
    private const int Fusion = 10;
    private const double TextWeight = 2;
    private const double VectorWeight = 1;
    private const double GraphWeight = 13d / 132d;
    private const long Revision = 1;
    private const int FirstRank = 1;
    private const int NoRank = 0;
    private const int SnapshotLimit = 4096;
    private static readonly string[] Text = [A, B, C];
    private static readonly string[] Vector = [B, A, C, D];
    private static readonly string[] Graph = [D, C, B];

    internal static TestDatabase Create(TimeProvider? clock = null)
    {
        var db = new TestDatabase(timeProvider: clock);
        ThreeWayHybridTestSupport.Configure(db);
        ThreeWayHybridTestSupport.Seed(db);
        ThreeWayHybridTestSupport.PersistReader(db, true);
        ThreeWayHybridTestSupport.PersistReader(db, false);
        return db;
    }

    internal static GraphSearchRequest Request(TestDatabase db, bool restricted)
    {
        var original = ThreeWayHybridTestSupport.Request(db.Partition,
            restricted ? [B, C, D] : null, scoped: restricted);
        return original with { Search = original.Search with { Explain = true } };
    }

    internal static async Task LiteralAsync(GraphSearchResult result, PartitionRef partition, bool restricted)
    {
        var ids = restricted ? new[] { B, C } : new[] { A, B, C, D };
        await Assert.That(result.Hits.Select(hit => hit.Document.Reference.Id).ToArray())
            .IsEquivalentTo(ids, CollectionOrdering.Matching);
        for (var index = 0; index < ids.Length; index++)
        {
            var parts = new List<SearchBranchContribution>();
            Add(Text, SearchBranchKind.Text, TextWeight);
            Add(Vector, SearchBranchKind.Vector, VectorWeight);
            Add(Graph, SearchBranchKind.Graph, GraphWeight);
            var score = 0d;
            foreach (var part in parts)
            { score += part.Contribution; }
            var expected = new RankedDocument(new(new(partition, ThreeWayHybridTestSupport.Documents,
                ids[index]), Revision, "{}", true, [TextField, VectorField]), score, new(Fusion, [.. parts]));
            await Assert.That(JsonDefaults.Serialize(result.Hits[index]).AsSpan()
                .SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();

            void Add(string[] order, SearchBranchKind branch, double weight)
            {
                var eligible = order.Where(ids.Contains).ToArray();
                var rank = Array.IndexOf(eligible, ids[index]) + FirstRank;
                if (rank > NoRank)
                { parts.Add(new(branch, rank, weight, weight / (Fusion + (double)rank))); }
            }
        }
        await Assert.That(result.Expansion).IsNull();
    }

    internal static (string Key, string Value)[] Snapshot(TestDatabase db) => db.Store.Read(view =>
    {
        var page = view.Scan([], SnapshotLimit);
        if (page.HasMore)
        { throw new InvalidOperationException("The complete hybrid Explain fixture exceeds its snapshot bound."); }
        return page.Records.Select(row => (Convert.ToHexString(row.Key.Span), Convert.ToHexString(row.Value.Span))).ToArray();
    });
}
