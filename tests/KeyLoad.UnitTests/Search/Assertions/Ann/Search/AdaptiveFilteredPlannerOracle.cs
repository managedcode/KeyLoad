using KeyLoad.Query.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal static class AdaptiveFilteredPlannerOracle
{
    private const double ScoreTolerance = 0.000000000005;
    private const int TenPercentDivisor = 10;
    private const int OnePercentDivisor = 100;
    private const int PointOnePercentDivisor = 1_000;
    private const int CorrelatedEligibleCount = PackedAnnTestData.QualityRecordCount / TenPercentDivisor;

    internal static AdaptiveFilteredCohort[] Cohorts(VectorRecord[] records, float[] query)
    {
        var scores = Scores(records, query);
        return
        [
            new("100-percent", Enumerable.Range(0, records.Length).ToArray()),
            new("10-percent", Every(records.Length, TenPercentDivisor)),
            new("1-percent", Every(records.Length, OnePercentDivisor)),
            new("0.1-percent", Every(records.Length, PointOnePercentDivisor)),
            new("correlated-nearest-10-percent", scores.OrderByDescending(item => item.Score)
                .ThenBy(item => item.Id, StringComparer.Ordinal).Take(CorrelatedEligibleCount)
                .Select(item => item.SourceOrdinal).Order().ToArray())
        ];
    }

    internal static (int SourceOrdinal, string Id, double Score)[] Rank(VectorRecord[] records,
        float[] query, int[] eligible)
        => eligible.Select(ordinal => Score(records[ordinal], ordinal, query))
            .OrderByDescending(item => item.Score).ThenBy(item => item.Id, StringComparer.Ordinal).ToArray();

    internal static ulong[] Bitmap(int count, int[] eligible)
    {
        var bitmap = new ulong[(count + 63) / 64];
        foreach (var ordinal in eligible)
        {
            bitmap[ordinal >> 6] |= 1UL << (ordinal & 63);
        }
        return bitmap;
    }

    internal static async Task AssertReturnedCandidatesAsync(AnnSearchResult result, VectorRecord[] records,
        float[] query, int[] eligible)
    {
        var eligibleSet = eligible.ToHashSet();
        for (var index = 0; index < result.Candidates.Length; index++)
        {
            var candidate = result.Candidates[index];
            await Assert.That(eligibleSet.Contains(candidate.SourceOrdinal)).IsTrue();
            await Assert.That(candidate.DocumentId).IsEqualTo(records[candidate.SourceOrdinal].DocumentId);
            await Assert.That(candidate.DocumentRevision).IsEqualTo(records[candidate.SourceOrdinal].DocumentRevision);
            await Assert.That(candidate.Score).IsEqualTo(Score(records[candidate.SourceOrdinal],
                candidate.SourceOrdinal, query).Score).Within(ScoreTolerance);
            await Assert.That(double.IsFinite(candidate.Score)).IsTrue();
            if (index > 0)
            {
                var previous = result.Candidates[index - 1];
                await Assert.That(previous.Score > candidate.Score || previous.Score == candidate.Score
                    && StringComparer.Ordinal.Compare(previous.DocumentId, candidate.DocumentId) <= 0).IsTrue();
            }
        }
    }

    private static (int SourceOrdinal, string Id, double Score)[] Scores(VectorRecord[] records, float[] query)
        => records.Select((record, ordinal) => Score(record, ordinal, query)).ToArray();

    internal static (int SourceOrdinal, string Id, double Score) Score(VectorRecord record,
        int ordinal, float[] query)
    {
        double total = 0;
        for (var component = 0; component < query.Length; component++)
        {
            total += (double)query[component] * record.Values[component];
        }
        return (ordinal, record.DocumentId, total);
    }

    private static int[] Every(int count, int divisor)
        => Enumerable.Range(0, count).Where(ordinal => ordinal % divisor == 0).ToArray();
}
