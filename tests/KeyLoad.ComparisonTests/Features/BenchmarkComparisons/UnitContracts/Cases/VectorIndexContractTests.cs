using KeyLoad.Comparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class VectorIndexContractTests
{
    [Test]
    [Arguments("hnsw")]
    [Arguments("ivfflat")]
    [Arguments("native")]
    public async Task ApproximateIndexReceiptsRequirePositiveFiniteBuildDuration(string method)
    {
        var profile = VectorComparisonProfile.Parse($"vector-100k-{method}-plain-c16");
        var valid = new VectorIndexReceipt(profile.IndexKind, "native persisted definition", new Dictionary<string, string>(), 1d);
        VectorComparisonRunner.ValidateIndex(profile, valid, method);
        foreach (var duration in new[] { 0d, -1d, double.NaN, double.PositiveInfinity })
        {
            await Assert.That(() => VectorComparisonRunner.ValidateIndex(profile, valid with { BuildMilliseconds = duration }, method))
                .Throws<InvalidDataException>();
        }
    }

    [Test]
    public async Task ExactReceiptsRequireZeroBuildTimeAndAnnPlansMustNameTheSelectedAlgorithm()
    {
        var exact = VectorComparisonProfile.Parse("vector-100k-exact-plain-c16");
        var receipt = new VectorIndexReceipt(VectorIndexKind.Exact, "full scan", new Dictionary<string, string>(), 0d);
        VectorComparisonRunner.ValidateIndex(exact, receipt, "native full scan");
        await Assert.That(() => VectorComparisonRunner.ValidateIndex(exact, receipt with { BuildMilliseconds = 1d }, "native full scan"))
            .Throws<InvalidDataException>();
        var hnsw = VectorComparisonProfile.Parse("vector-100k-hnsw-plain-c16");
        await Assert.That(() => VectorComparisonRunner.ValidateIndex(hnsw,
            receipt with { IndexKind = VectorIndexKind.Hnsw, BuildMilliseconds = 1d }, "sequential scan"))
            .Throws<InvalidDataException>();
    }
}
