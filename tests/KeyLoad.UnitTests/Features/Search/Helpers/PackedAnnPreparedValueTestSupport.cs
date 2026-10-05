using System.Runtime.InteropServices;
using KeyLoad.Query;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal static class PackedAnnPreparedValueTestSupport
{
    internal static TestDatabase CreateDatabase()
    {
        var database = new TestDatabase();
        PackedAnnTestData.Configure(database);
        return database;
    }

    internal static VectorRecord[] PersistAndLoad(TestDatabase database, int dimension, DistanceMetric metric,
        float[][]? vectors = null, string field = "")
    {
        var source = vectors ?? PackedAnnOwnedSimilarityTestSupport.Vectors(dimension);
        var selectedField = field.Length == 0 ? PackedAnnTestData.Field(metric) : field;
        PackedAnnIndexTestSupport.PersistVectors(database, PackedAnnTestData.Space(metric, dimension),
            selectedField, source);
        return database.Database.WithVectors(PackedAnnTestData.Principal, database.Partition,
            PackedAnnTestData.Collection, selectedField,
            (_, _, pairs) => pairs.Select(pair => pair.Vector)
                .OrderBy(record => record.DocumentId, StringComparer.Ordinal).ToArray());
    }

    internal static PackedAnnVectors Copy(TestDatabase database, VectorRecord[] records, VectorSpace space)
    {
        var budget = PackedAnnIndexTestSupport.Budget(database);
        var admission = PackedAnnAdmission.Create(space, records, new PackedAnnOptions(), budget);
        return PackedAnnVectors.Copy(records, admission, budget);
    }

    internal static async Task VerifyScoresAsync(TestDatabase database, float[][] vectors,
        DistanceMetric metric, int dimension)
    {
        var records = PersistAndLoad(database, dimension, metric, vectors);
        var oracle = records.Select(record => record.Values.ToArray()).ToArray();
        var packed = Copy(database, records, PackedAnnTestData.Space(metric, dimension));
        MutateOneSource(records);
        for (var query = 0; query < packed.Count; query++)
        {
            var prepared = PreparedPackedSimilarity.Create(packed, query, metric);
            for (var candidate = 0; candidate < packed.Count; candidate++)
            {
                var expected = SearchEngine.Similarity(oracle[query], oracle[candidate], metric);
                await Assert.That(prepared.Score(packed, candidate)).IsEqualTo(expected);
            }
        }
    }

    internal static double RunScoringLoop(PackedAnnVectors packed, DistanceMetric metric, int repetitions)
    {
        double total = 0;
        for (var repetition = 0; repetition < repetitions; repetition++)
        {
            for (var query = 0; query < packed.Count; query++)
            {
                var prepared = PreparedPackedSimilarity.Create(packed, query, metric);
                for (var candidate = 0; candidate < packed.Count; candidate++)
                { total += prepared.Score(packed, candidate); }
            }
        }
        return total;
    }

    internal static async Task AssertValidationAsync(Action operation)
    {
        var failure = Assert.ThrowsExactly<KeyLoadException>(operation);
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Validation);
    }

    internal static async Task AssertUntrustedValidationAsync()
    {
        await AssertValidationAsync(() => PreparedSimilarity.Create(
            new[] { float.NaN }.AsMemory(), DistanceMetric.Cosine));
        var valid = PreparedSimilarity.Create(new[] { 1f }.AsMemory(), DistanceMetric.Cosine);
        await AssertValidationAsync(() => valid.Score(new[] { float.PositiveInfinity }.AsMemory()));
    }

    private static void MutateOneSource(VectorRecord[] records)
    {
        var source = ImmutableCollectionsMarshal.AsArray(records[0].Values)!;
        source[0] = -17f;
    }
}
