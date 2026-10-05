using System.Numerics;
using System.Runtime.InteropServices;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class PackedAnnPreparedValueOracleTests
{
    private static readonly DistanceMetric[] Metrics =
        [DistanceMetric.Cosine, DistanceMetric.Euclidean, DistanceMetric.DotProduct];
    private static readonly int[] Dimensions = CreateDimensions();

    [Test]
    public async Task AcAnn010PreparedPackedScoresMatchIndependentPersistedVectorOracle()
    {
        foreach (var dimension in Dimensions)
        {
            var vectors = PackedAnnOwnedSimilarityTestSupport.Vectors(dimension);
            foreach (var metric in Metrics)
            {
                using var database = PackedAnnPreparedValueTestSupport.CreateDatabase();
                await PackedAnnPreparedValueTestSupport.VerifyScoresAsync(database, vectors, metric, dimension);
            }
        }
    }

    [Test]
    public async Task AcAnn010PreparedPackedInputsRetainTypedValidation()
    {
        using var database = PackedAnnPreparedValueTestSupport.CreateDatabase();
        var records = PackedAnnPreparedValueTestSupport.PersistAndLoad(database, 8, DistanceMetric.Cosine);
        var packed = PackedAnnPreparedValueTestSupport.Copy(database, records,
            PackedAnnTestData.Space(DistanceMetric.Cosine, 8));
        var value = PreparedPackedSimilarity.Create(packed, 0, DistanceMetric.Cosine);

        await PackedAnnPreparedValueTestSupport.AssertValidationAsync(
            () => PreparedPackedSimilarity.Create(packed, packed.Count, DistanceMetric.Cosine));
        await PackedAnnPreparedValueTestSupport.AssertValidationAsync(
            () => PreparedPackedSimilarity.Create(packed, 0, (DistanceMetric)99));
        await PackedAnnPreparedValueTestSupport.AssertValidationAsync(
            () => value.Score(packed, packed.Count));
        await PackedAnnPreparedValueTestSupport.AssertUntrustedValidationAsync();

        using var nonFiniteDatabase = PackedAnnPreparedValueTestSupport.CreateDatabase();
        var invalidRecords = PackedAnnPreparedValueTestSupport.PersistAndLoad(nonFiniteDatabase, 8,
            DistanceMetric.Euclidean);
        ImmutableCollectionsMarshal.AsArray(invalidRecords[0].Values)![0] = float.NaN;
        await PackedAnnPreparedValueTestSupport.AssertValidationAsync(() =>
            PackedAnnPreparedValueTestSupport.Copy(nonFiniteDatabase, invalidRecords,
                PackedAnnTestData.Space(DistanceMetric.Euclidean, 8)));
    }

    [Test]
    public async Task AcAnn010PreparedValueRejectsAnotherPackedDimension()
    {
        using var firstDatabase = PackedAnnPreparedValueTestSupport.CreateDatabase();
        using var secondDatabase = PackedAnnPreparedValueTestSupport.CreateDatabase();
        var firstRecords = PackedAnnPreparedValueTestSupport.PersistAndLoad(firstDatabase, 1, DistanceMetric.DotProduct);
        var secondRecords = PackedAnnPreparedValueTestSupport.PersistAndLoad(secondDatabase, 2, DistanceMetric.DotProduct);
        var firstPacked = PackedAnnPreparedValueTestSupport.Copy(firstDatabase, firstRecords,
            PackedAnnTestData.Space(DistanceMetric.DotProduct, 1));
        var secondPacked = PackedAnnPreparedValueTestSupport.Copy(secondDatabase, secondRecords,
            PackedAnnTestData.Space(DistanceMetric.DotProduct, 2));
        var value = PreparedPackedSimilarity.Create(firstPacked, 0, DistanceMetric.DotProduct);

        await PackedAnnPreparedValueTestSupport.AssertValidationAsync(() => value.Score(secondPacked, 0));
    }

    [Test, NotInParallel]
    public async Task AcAnn010WarmedPreparedPackedConstructionAndScoringAllocateZeroBytes()
    {
        const int dimension = 4_096;
        foreach (var metric in Metrics)
        {
            using var database = PackedAnnPreparedValueTestSupport.CreateDatabase();
            var records = PackedAnnPreparedValueTestSupport.PersistAndLoad(database, dimension, metric);
            var packed = PackedAnnPreparedValueTestSupport.Copy(database, records,
                PackedAnnTestData.Space(metric, dimension));
            _ = PackedAnnPreparedValueTestSupport.RunScoringLoop(packed, metric, 2);
            _ = PackedAnnPreparedValueTestSupport.RunScoringLoop(packed, metric, 2);
            var expected = PackedAnnPreparedValueTestSupport.RunScoringLoop(packed, metric, 8);
            var before = GC.GetAllocatedBytesForCurrentThread();
            var measured = PackedAnnPreparedValueTestSupport.RunScoringLoop(packed, metric, 8);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            await Assert.That(measured).IsEqualTo(expected);
            await Assert.That(allocated).IsEqualTo(0L);
        }
    }

    private static int[] CreateDimensions()
    {
        var lane = Vector<float>.Count;
        int[] dimensions = [1, Math.Max(1, lane - 1), lane, lane + 1, 4_096];
        return dimensions.Distinct().ToArray();
    }
}
