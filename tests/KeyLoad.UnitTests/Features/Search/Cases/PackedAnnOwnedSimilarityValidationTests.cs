using KeyLoad.Core;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class PackedAnnOwnedSimilarityValidationTests
{
    [Test]
    public async Task AcAnn009PackedFactoriesAndUntrustedScoringRetainTypedValidation()
    {
        using var database = PackedAnnOwnedSimilarityTestSupport.CreateDatabase();
        const DistanceMetric metric = DistanceMetric.Cosine;
        var dimension = 4_096;
        var space = PackedAnnTestData.Space(metric, dimension);
        var source = PackedAnnOwnedSimilarityTestSupport.Vectors(dimension);
        PackedAnnIndexTestSupport.PersistVectors(database, space, PackedAnnTestData.Field(metric), source);
        var records = PackedAnnTestData.Load(database, metric);
        var budget = PackedAnnIndexTestSupport.Budget(database);
        var options = new PackedAnnOptions();
        var layout = PackedAnnAdmission.Create(space, records, options, budget);
        var packed = PackedAnnVectors.Copy(records, layout, budget);

        await PackedAnnOwnedSimilarityAssertions.AssertValidationAsync(
            () => PreparedSimilarity.CreatePacked(packed, packed.Count, metric));
        await PackedAnnOwnedSimilarityAssertions.AssertValidationAsync(
            () => PreparedSimilarity.CreatePacked(packed, 0, (DistanceMetric)99));
        var validPackedQuery = PreparedSimilarity.CreatePacked(packed, 0, metric);
        await PackedAnnOwnedSimilarityAssertions.AssertValidationAsync(
            () => validPackedQuery.ScorePacked(packed, packed.Count));
        var wrongDimensionQuery = PreparedSimilarity.Create(new float[dimension - 1].AsMemory(), metric);
        await PackedAnnOwnedSimilarityAssertions.AssertValidationAsync(
            () => wrongDimensionQuery.ScorePacked(packed, 0));

        await PackedAnnOwnedSimilarityAssertions.AssertValidationAsync(() =>
            PreparedSimilarity.Create(new[] { float.NaN }.AsMemory(), metric));
        await PackedAnnOwnedSimilarityAssertions.AssertValidationAsync(() =>
            PreparedSimilarity.Create(new float[4_097].AsMemory(), metric));
        await PackedAnnOwnedSimilarityAssertions.AssertValidationAsync(() =>
            PreparedSimilarity.Create(new[] { 1f }.AsMemory(), (DistanceMetric)99));
        var untrusted = PreparedSimilarity.Create(new[] { 1f }.AsMemory(), metric);
        await PackedAnnOwnedSimilarityAssertions.AssertValidationAsync(
            () => untrusted.Score(new[] { float.PositiveInfinity }.AsMemory()));

        var mutable = System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsArray(records[1].Values)!;
        mutable[0] = float.NaN;
        await PackedAnnOwnedSimilarityAssertions.AssertValidationAsync(
            () => PackedAnnVectors.Copy(records, layout, PackedAnnIndexTestSupport.Budget(database)));
    }
}
