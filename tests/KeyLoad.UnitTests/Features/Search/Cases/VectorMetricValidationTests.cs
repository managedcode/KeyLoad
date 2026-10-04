using System.Numerics;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class VectorMetricValidationTests
{
    private const string InvalidVectorDetail = "Vector dimensions or values are invalid.";
    private const string InvalidMetricDetail = "The vector metric is invalid.";
    private const int MaximumDimension = 4_096;
    private const int InvalidMetricValue = int.MaxValue;
    private static readonly float[] ValidVector = [1];
    private static readonly float[] NonfiniteValues = [float.NaN, float.PositiveInfinity, float.NegativeInfinity];

    [Test]
    public async Task AcSearch001SimilarityPreservesLengthMetricAndValidationOrderDetails()
    {
        var oversized = new float[MaximumDimension + 1];
        var invalidMetric = (DistanceMetric)InvalidMetricValue;

        await AssertInvalid(() => SearchEngine.Similarity([], ValidVector, DistanceMetric.DotProduct), InvalidVectorDetail);
        await AssertInvalid(() => SearchEngine.Similarity(oversized, oversized, DistanceMetric.DotProduct), InvalidVectorDetail);
        await AssertInvalid(() => SearchEngine.Similarity(ValidVector, oversized, DistanceMetric.DotProduct), InvalidVectorDetail);
        await AssertInvalid(() => SearchEngine.Similarity(ValidVector, [], DistanceMetric.DotProduct), InvalidVectorDetail);
        await AssertInvalid(() => SearchEngine.Similarity([1, 2], ValidVector, DistanceMetric.DotProduct), InvalidVectorDetail);
        await AssertInvalid(() => SearchEngine.Similarity(ValidVector, ValidVector, invalidMetric), InvalidMetricDetail);
        await AssertInvalid(() => SearchEngine.Similarity([float.NaN], ValidVector, invalidMetric), InvalidVectorDetail);
    }

    [Test]
    public async Task AcSearch001SimilarityRejectsNonfiniteQueryAndCandidateAtEveryBlockLaneAndTail()
    {
        var width = Vector<float>.Count;
        var values = new float[(2 * width) + 1];

        foreach (var invalid in NonfiniteValues)
        {
            for (var index = 0; index < values.Length; index++)
            {
                var query = (float[])values.Clone();
                query[index] = invalid;
                await AssertInvalid(() => SearchEngine.Similarity(query, values, DistanceMetric.DotProduct), InvalidVectorDetail);

                var candidate = (float[])values.Clone();
                candidate[index] = invalid;
                await AssertInvalid(() => SearchEngine.Similarity(values, candidate, DistanceMetric.DotProduct), InvalidVectorDetail);
            }
        }
    }

    private static async Task AssertInvalid(Func<double> operation, string detail)
    {
        var error = Assert.ThrowsExactly<KeyLoadException>(() => _ = operation());
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(error.Message).IsEqualTo(detail);
    }
}
