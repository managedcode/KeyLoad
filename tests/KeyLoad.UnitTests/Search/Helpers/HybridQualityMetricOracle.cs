using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.Search;

internal static class HybridQualityMetricOracle
{
    private const int RecallFive = 5;
    private const int RecallTen = 10;
    private const int ReciprocalRankTen = 10;
    private const int NdcgFive = 5;
    private const int NdcgTen = 10;

    internal static HybridQualityMetrics Compute(IReadOnlyList<string> rankedIds,
        ImmutableArray<HybridQualityJudgment> judgments, ImmutableArray<string> eligibleIds)
    {
        ArgumentNullException.ThrowIfNull(rankedIds);
        var grades = BuildGrades(judgments);
        var eligible = BuildEligible(eligibleIds);
        ValidateRankedIds(rankedIds);
        var relevant = eligible.Count(id => grades.GetValueOrDefault(id) > 0);
        var result = new HybridQualityMetrics(
            Recall(rankedIds, eligible, grades, relevant, RecallFive),
            Recall(rankedIds, eligible, grades, relevant, RecallTen),
            MeanReciprocalRank(rankedIds, eligible, grades, relevant),
            Ndcg(rankedIds, eligible, grades, relevant, NdcgFive),
            Ndcg(rankedIds, eligible, grades, relevant, NdcgTen));
        ValidateMetrics(result);
        return result;
    }

    internal static double CandidateRecall(IReadOnlyCollection<string> candidateIds,
        ImmutableArray<HybridQualityJudgment> judgments, ImmutableArray<string> eligibleIds)
    {
        ArgumentNullException.ThrowIfNull(candidateIds);
        var grades = BuildGrades(judgments);
        var eligible = BuildEligible(eligibleIds);
        var relevant = eligible.Where(id => grades.GetValueOrDefault(id) > 0).ToArray();
        return relevant.Length == 0 ? 0 : relevant.Count(candidateIds.Contains) / (double)relevant.Length;
    }

    private static Dictionary<string, int> BuildGrades(ImmutableArray<HybridQualityJudgment> judgments)
    {
        if (judgments.IsDefault)
        {
            throw new ArgumentException("Judgments must be initialized.", nameof(judgments));
        }
        var grades = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var judgment in judgments)
        {
            if (string.IsNullOrEmpty(judgment.Id) || judgment.Grade is < 0 or > 3
                || !grades.TryAdd(judgment.Id, judgment.Grade))
            {
                throw new ArgumentException("Judgments must have unique IDs and grades from zero through three.", nameof(judgments));
            }
        }
        return grades;
    }

    private static HashSet<string> BuildEligible(ImmutableArray<string> eligibleIds)
    {
        if (eligibleIds.IsDefault)
        {
            throw new ArgumentException("Eligible IDs must be initialized.", nameof(eligibleIds));
        }
        var eligible = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in eligibleIds)
        {
            if (string.IsNullOrEmpty(id) || !eligible.Add(id))
            {
                throw new ArgumentException("Eligible IDs must be unique and nonempty.", nameof(eligibleIds));
            }
        }
        return eligible;
    }

    private static void ValidateRankedIds(IReadOnlyList<string> rankedIds)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in rankedIds)
        {
            if (string.IsNullOrEmpty(id) || !seen.Add(id))
            {
                throw new ArgumentException("Ranked IDs must be unique and nonempty.", nameof(rankedIds));
            }
        }
    }

    private static double Recall(IReadOnlyList<string> rankedIds, HashSet<string> eligible,
        Dictionary<string, int> grades, int relevant, int limit)
    {
        if (relevant == 0)
        {
            return 0;
        }
        var found = 0;
        for (var index = 0; index < Math.Min(limit, rankedIds.Count); index++)
        {
            var id = rankedIds[index];
            if (eligible.Contains(id) && grades.GetValueOrDefault(id) > 0)
            {
                found++;
            }
        }
        return found / (double)relevant;
    }

    private static double MeanReciprocalRank(IReadOnlyList<string> rankedIds, HashSet<string> eligible,
        Dictionary<string, int> grades, int relevant)
    {
        if (relevant == 0)
        {
            return 0;
        }
        for (var index = 0; index < Math.Min(ReciprocalRankTen, rankedIds.Count); index++)
        {
            var id = rankedIds[index];
            if (eligible.Contains(id) && grades.GetValueOrDefault(id) > 0)
            {
                return 1d / (index + 1);
            }
        }
        return 0;
    }

    private static double Ndcg(IReadOnlyList<string> rankedIds, HashSet<string> eligible,
        Dictionary<string, int> grades, int relevant, int limit)
    {
        if (relevant == 0)
        {
            return 0;
        }
        var actual = DiscountedGain(rankedIds.Take(limit).Select(id => eligible.Contains(id)
            ? grades.GetValueOrDefault(id) : 0));
        var ideal = DiscountedGain(eligible.Select(id => grades.GetValueOrDefault(id))
            .OrderByDescending(grade => grade).Take(limit));
        return actual / ideal;
    }

    private static double DiscountedGain(IEnumerable<int> grades)
        => grades.Select((grade, index) => (Math.Pow(2, grade) - 1) / Math.Log2(index + 2)).Sum();

    private static void ValidateMetrics(HybridQualityMetrics metrics)
    {
        foreach (var value in new[] { metrics.RecallAt5, metrics.RecallAt10, metrics.MeanReciprocalRankAt10,
                     metrics.NdcgAt5, metrics.NdcgAt10 })
        {
            if (!double.IsFinite(value) || value is < 0 or > 1)
            {
                throw new InvalidOperationException("Quality metrics must be finite and within zero through one.");
            }
        }
    }
}
