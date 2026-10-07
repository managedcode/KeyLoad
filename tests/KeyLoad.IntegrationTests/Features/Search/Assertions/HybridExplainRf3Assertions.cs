using TUnit.Assertions.Enums;
using static KeyLoad.IntegrationTests.Features.Search.ThreeWayHybridRf3Scenario;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class HybridExplainRf3Assertions
{
    private const int Fusion = 10;
    private const double TextWeight = 2;
    private const double VectorWeight = 1;
    private const double GraphWeight = 13d / 132d;
    private const long Revision = 1;
    private const int FirstRank = 1;
    private const int NoRank = 0;
    private static readonly string[] Text = [A, B, C];
    private static readonly string[] Vector = [B, A, C, D];
    private static readonly string[] Graph = [D, C, B];

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
            var expected = new RankedDocument(new(new(partition, ThreeWayHybridRf3Scenario.Documents,
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

}
