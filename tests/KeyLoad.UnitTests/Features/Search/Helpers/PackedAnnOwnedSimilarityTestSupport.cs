using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.Search;

internal static class PackedAnnOwnedSimilarityTestSupport
{
    private const int OrdinaryVectorOrdinal = 23;
    private const int SubnormalPosition = 0;

    internal static TestDatabase CreateDatabase()
    {
        var database = new TestDatabase();
        PackedAnnTestData.Configure(database);
        return database;
    }

    internal static float[][] Vectors(int dimension)
    {
        var zero = new float[dimension];
        var signedZero = new float[dimension];
        signedZero[SubnormalPosition] = -0f;
        var subnormal = new float[dimension];
        subnormal[SubnormalPosition] = float.Epsilon;
        if (dimension > 1)
        {
            subnormal[^1] = -float.Epsilon;
        }
        var ordinary = PackedAnnTestData.Vector(OrdinaryVectorOrdinal, dimension, PackedAnnTestData.CorpusSeed);
        var maximum = Enumerable.Repeat(float.MaxValue, dimension).ToArray();
        var minimum = Enumerable.Repeat(-float.MaxValue, dimension).ToArray();
        return [zero, signedZero, subnormal, ordinary, maximum, minimum];
    }
}
