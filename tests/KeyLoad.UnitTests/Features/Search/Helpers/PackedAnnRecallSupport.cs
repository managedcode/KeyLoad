using System.Text.Json;
using KeyLoad.Query;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal readonly record struct PackedAnnRecallCell(string Name, int Modulus, bool GeometryCorrelated);

internal readonly record struct PackedAnnRecallObservation(double Recall, int QueryCount,
    HashSet<AnnSearchMode> Modes, long WorkUnits, long Distances, long Edges);

internal static class PackedAnnRecallSupport
{
    internal static readonly PackedAnnRecallCell[] Cells =
    [
        new("all", 1, false), new("10-percent", 10, false), new("1-percent", 100, false),
        new("0.1-percent", 1_000, false), new("geometry-correlated-10-percent", 0, true)
    ];

    internal static async Task<PackedAnnRecallObservation> MeasureCell(TestDatabase database,
        VectorRecord[] records, PackedAnnIndex index, DistanceMetric metric, PackedAnnRecallCell cell)
    {
        var modes = new HashSet<AnnSearchMode>();
        long relevant = 0;
        long work = 0;
        long distances = 0;
        long edges = 0;
        var queries = Queries(metric);
        await Assert.That(queries.Select(QueryIdentity).Distinct(StringComparer.Ordinal).Count())
            .IsEqualTo(PackedAnnTestData.QualityQueryCount);
        for (var queryNumber = 0; queryNumber < queries.Length; queryNumber++)
        {
            var query = queries[queryNumber];
            var allScores = ScoreAll(records, query, metric);
            var eligible = Eligible(allScores, cell);
            var expectedEligible = cell.GeometryCorrelated ? records.Length / 10 : records.Length / cell.Modulus;
            await Assert.That(eligible.Length).IsEqualTo(expectedEligible);
            var bitmap = Bitmap(records.Length, eligible);
            var exact = Rank(allScores, eligible);
            var result = index.Search(query, PackedAnnTestData.TopK, bitmap,
                PackedAnnIndexTestSupport.Budget(database));
            modes.Add(result.Mode);
            work = checked(work + result.WorkUnits);
            distances = checked(distances + result.DistanceEvaluations);
            edges = checked(edges + result.EdgeVisits);
            relevant += result.Candidates.Count(candidate => exact.Take(PackedAnnTestData.TopK)
                .Any(expected => expected.Id == candidate.DocumentId));
            await Assert.That(result.Candidates.Length).IsEqualTo(PackedAnnTestData.TopK);
            foreach (var candidate in result.Candidates)
            {
                await Assert.That(Array.BinarySearch(eligible, candidate.SourceOrdinal) >= 0).IsTrue();
                await Assert.That(candidate.Score).IsEqualTo(allScores[candidate.SourceOrdinal].Score);
                await Assert.That(candidate.DocumentId).IsEqualTo(records[candidate.SourceOrdinal].DocumentId);
                await Assert.That(candidate.DocumentRevision).IsEqualTo(records[candidate.SourceOrdinal].DocumentRevision);
            }
        }

        return new((double)relevant / (PackedAnnTestData.QualityQueryCount * PackedAnnTestData.TopK),
            PackedAnnTestData.QualityQueryCount, modes, work, distances, edges);
    }

    internal static async Task AssertCell(PackedAnnRecallObservation observed, DistanceMetric metric,
        PackedAnnRecallCell cell, PackedAnnOptions options)
    {
        WriteCellObservation(observed, metric, cell, options);
        var metadata = new QualityMetadata(metric, cell.Name, PackedAnnTestData.QualityRecordCount,
            observed.QueryCount, PackedAnnTestData.TopK, PackedAnnTestData.CorpusSeed);
        await Assert.That(metadata).IsEqualTo(new QualityMetadata(metric, cell.Name,
            PackedAnnTestData.QualityRecordCount, PackedAnnTestData.QualityQueryCount,
            PackedAnnTestData.TopK, PackedAnnTestData.CorpusSeed));
        await Assert.That(observed.Recall).IsGreaterThanOrEqualTo(0.95);
        var eligibleCount = cell.GeometryCorrelated
            ? PackedAnnTestData.QualityRecordCount / 10
            : PackedAnnTestData.QualityRecordCount / cell.Modulus;
        await Assert.That(observed.WorkUnits > 0 && observed.Distances > 0).IsTrue();
        if (eligibleCount <= 256)
        {
            await Assert.That(observed.Edges).IsEqualTo(0L);
        }
        else
        {
            await Assert.That(observed.Edges).IsGreaterThan(0);
        }
        var expectedMode = eligibleCount <= 256
            ? AnnSearchMode.ExactSmallSet
            : AnnSearchMode.Approximate;
        await Assert.That(observed.Modes.Contains(expectedMode)
            || observed.Modes.Contains(AnnSearchMode.ExactAfterInsufficientCandidates)).IsTrue();
    }

    private static void WriteCellObservation(PackedAnnRecallObservation observed, DistanceMetric metric,
        PackedAnnRecallCell cell, PackedAnnOptions options)
        => Console.WriteLine(JsonSerializer.Serialize(new
        {
            metric,
            cell = cell.Name,
            recordCount = PackedAnnTestData.QualityRecordCount,
            queryCount = observed.QueryCount,
            dimension = PackedAnnTestData.QualityDimension,
            topK = PackedAnnTestData.TopK,
            seed = PackedAnnTestData.CorpusSeed,
            recall = observed.Recall,
            modes = observed.Modes.Order().Select(mode => mode.ToString()).ToArray(),
            workUnits = observed.WorkUnits,
            distanceEvaluations = observed.Distances,
            edgeVisits = observed.Edges,
            settings = new
            {
                options.Connections,
                options.EfConstruction,
                options.EfSearch,
                options.MaxLevel,
                options.ExactThreshold,
                options.MaxRecords,
                options.MaxIndexBytes,
                options.MaxScratchBytes,
                options.Seed
            }
        }));

    private static float[][] Queries(DistanceMetric metric)
        => Enumerable.Range(0, PackedAnnTestData.QualityQueryCount)
            .Select(number => PackedAnnTestData.Vector(20_000 + number, PackedAnnTestData.QualityDimension,
                PackedAnnTestData.CorpusSeed ^ (ulong)metric)).ToArray();

    private static string QueryIdentity(float[] query)
        => string.Join(",", query.Select(BitConverter.SingleToInt32Bits));

    private static ScoredOrdinal[] ScoreAll(VectorRecord[] records, float[] query, DistanceMetric metric)
        => records.Select((record, ordinal) => new ScoredOrdinal(ordinal, record.DocumentId,
                SearchEngine.Similarity(query, record.Values.ToArray(), metric)))
            .ToArray();

    private static int[] Eligible(ScoredOrdinal[] scores, PackedAnnRecallCell cell)
        => cell.GeometryCorrelated
            ? scores.OrderByDescending(item => item.Score).ThenBy(item => item.Id, StringComparer.Ordinal)
                .Take(scores.Length / 10).Select(item => item.Ordinal).Order().ToArray()
            : Enumerable.Range(0, scores.Length).Where(ordinal => ordinal % cell.Modulus == 0).ToArray();

    private static ScoredOrdinal[] Rank(ScoredOrdinal[] scores, int[] eligible)
        => eligible.Select(ordinal => scores[ordinal]).OrderByDescending(item => item.Score)
            .ThenBy(item => item.Id, StringComparer.Ordinal).ToArray();

    private static ulong[] Bitmap(int count, int[] eligible)
    {
        var words = new ulong[(count + 63) / 64];
        foreach (var ordinal in eligible)
        {
            words[ordinal / 64] |= 1UL << (ordinal % 64);
        }
        return words;
    }

    private readonly record struct ScoredOrdinal(int Ordinal, string Id, double Score);
    private readonly record struct QualityMetadata(DistanceMetric Metric, string Cell, int Count, int Queries,
        int TopK, ulong Seed);
}
