using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class HybridQualityMetricOracleTests
{
    private static readonly ImmutableArray<HybridQualityJudgment> Graded =
    [new("a", 3), new("b", 2), new("c", 1), new("d", 0)];
    private static readonly ImmutableArray<string> Eligible = ["a", "b", "c", "d"];

    [Test]
    public async Task AcSearch007HandGoldensCoverPerfectReversedMissingAndTiedOrderings()
    {
        var perfect = HybridQualityMetricOracle.Compute(["a", "b", "c", "d"], Graded, Eligible);
        var reversed = HybridQualityMetricOracle.Compute(["d", "c", "b", "a"], Graded, Eligible);
        var missing = HybridQualityMetricOracle.Compute(["b", "unknown"], Graded, Eligible);
        var tiedJudgments = ImmutableArray.Create(new HybridQualityJudgment("a", 3),
            new HybridQualityJudgment("b", 3), new HybridQualityJudgment("c", 0));
        var tied = HybridQualityMetricOracle.Compute(["b", "a", "c"], tiedJudgments, ["a", "b", "c"]);

        await AssertMetricsAsync(perfect, 1, 1, 1, 1, 1);
        await AssertMetricsAsync(reversed, 1, 1, 0.5, 0.547831481922746, 0.547831481922746);
        await AssertMetricsAsync(missing, 1d / 3, 1d / 3, 1, 0.31939394323979897, 0.31939394323979897);
        await AssertMetricsAsync(tied, 1, 1, 1, 1, 1);
    }

    [Test]
    public async Task AcSearch007EmptyAndNoRelevantEligibleSetsReturnZeroAndRejectDuplicates()
    {
        var empty = HybridQualityMetricOracle.Compute([], Graded, Eligible);
        var noRelevant = HybridQualityMetricOracle.Compute(["a", "b"], Graded, ["d"]);
        var duplicate = Assert.ThrowsExactly<ArgumentException>(() =>
            HybridQualityMetricOracle.Compute(["a", "a"], Graded, Eligible));

        await AssertMetricsAsync(empty, 0, 0, 0, 0, 0);
        await AssertMetricsAsync(noRelevant, 0, 0, 0, 0, 0);
        await Assert.That(duplicate.ParamName).IsEqualTo("rankedIds");
    }

    private static async Task AssertMetricsAsync(HybridQualityMetrics actual, double recall5, double recall10,
        double mrr10, double ndcg5, double ndcg10)
    {
        await Assert.That(actual.RecallAt5).IsEqualTo(recall5).Within(0.000000000001);
        await Assert.That(actual.RecallAt10).IsEqualTo(recall10).Within(0.000000000001);
        await Assert.That(actual.MeanReciprocalRankAt10).IsEqualTo(mrr10).Within(0.000000000001);
        await Assert.That(actual.NdcgAt5).IsEqualTo(ndcg5).Within(0.000000000001);
        await Assert.That(actual.NdcgAt10).IsEqualTo(ndcg10).Within(0.000000000001);
    }
}
