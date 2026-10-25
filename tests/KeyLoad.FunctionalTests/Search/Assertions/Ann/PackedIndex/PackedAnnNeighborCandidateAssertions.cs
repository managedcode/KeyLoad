using KeyLoad.Query;
using KeyLoad.Query.Search;

namespace KeyLoad.FunctionalTests.Search;

internal static class PackedAnnNeighborCandidateAssertions
{
    private const int QueryOrdinal = 1_001;
    private const string Insufficient = "The bounded ANN graph did not supply the requested eligible candidates.";

    internal static async Task VerifyAsync(PackedAnnState state, int[][][] adjacency, VectorSpace space,
        VectorRecord[] records, PackedAnnOptions options, DistanceMetric metric)
    {
        var query = PackedAnnTestData.Vector(QueryOrdinal, space.Dimension, PackedAnnTestData.CorpusSeed);
        var scores = records.Select(record => SearchEngine.Similarity(query, record.Values.ToArray(), metric)).ToArray();
        var exact = records.Select((record, ordinal) => new AnnCandidate(ordinal, record.DocumentId,
                record.DocumentRevision, scores[ordinal]))
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.DocumentId, StringComparer.Ordinal).ToArray();
        var reachable = Reachable(adjacency, Entry(state, adjacency, scores));
        Console.WriteLine($"ANN graph: metric={metric}; vertices={state.Count}; reachable={reachable.Count}.");
        var index = PackedAnnIndex.Build(space, records, UnitExecutionOptions.PackedAnn(options),
            PackedAnnIndexTestSupport.Budget());
        var actual = index.Search(query, reachable.Count, null, PackedAnnIndexTestSupport.Budget());
        await Assert.That(actual.Mode).IsEqualTo(AnnSearchMode.Approximate);
        await EqualAsync(actual.Candidates, exact.Where(candidate => reachable.Contains(candidate.SourceOrdinal)).ToArray());
        if (reachable.Count < records.Length)
        {
            AnnSearchResult? partial = null;
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => partial = index.Search(query,
                records.Length, null, PackedAnnIndexTestSupport.Budget()));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
            await Assert.That(failure.Message).IsEqualTo(Insufficient);
            await Assert.That(partial).IsNull();
        }
        var complete = PackedAnnIndex.Build(space, records,
            UnitExecutionOptions.PackedAnn(options with { ExactThreshold = records.Length }),
            PackedAnnIndexTestSupport.Budget());
        var all = complete.Search(query, records.Length, null, PackedAnnIndexTestSupport.Budget());
        await Assert.That(all.Mode).IsEqualTo(AnnSearchMode.ExactSmallSet);
        await EqualAsync(all.Candidates, exact);
        await EqualAsync(index.Search(query, reachable.Count, null, PackedAnnIndexTestSupport.Budget()).Candidates,
            exact.Where(candidate => reachable.Contains(candidate.SourceOrdinal)).ToArray());
    }

    private static int Entry(PackedAnnState state, int[][][] adjacency, double[] scores)
    {
        var current = state.EntryPoint;
        for (var layer = state.MaximumLevel; layer > 0; layer--)
        {
            while (true)
            {
                var next = adjacency[current][layer].Append(current)
                    .OrderByDescending(ordinal => scores[ordinal]).ThenBy(ordinal => ordinal).First();
                if (next == current)
                {
                    break;
                }
                current = next;
            }
        }
        return current;
    }

    private static HashSet<int> Reachable(int[][][] adjacency, int entry)
    {
        HashSet<int> reached = [entry];
        Queue<int> pending = new([entry]);
        while (pending.TryDequeue(out var current))
        {
            foreach (var next in adjacency[current][0])
            {
                if (reached.Add(next))
                {
                    pending.Enqueue(next);
                }
            }
        }
        return reached;
    }

    private static async Task EqualAsync(AnnCandidate[] actual, AnnCandidate[] expected)
    {
        await Assert.That(actual.Length).IsEqualTo(expected.Length);
        for (var ordinal = 0; ordinal < expected.Length; ordinal++)
        {
            await Assert.That(actual[ordinal]).IsEqualTo(expected[ordinal]);
        }
    }
}
