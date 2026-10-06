using KeyLoad.Core;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal static class PackedAnnConstructionValueFixture
{
    internal const int RecordCount = 24;
    internal const int Dimension = 8;
    internal const long WorkLimit = 1_000_000_000;
    private static readonly DistanceMetric[] AllMetrics =
        [DistanceMetric.Cosine, DistanceMetric.Euclidean, DistanceMetric.DotProduct];

    internal static void Seed(TestDatabase database)
    {
        PackedAnnTestData.Configure(database);
        PackedAnnTestData.Seed(database, RecordCount, Dimension, AllMetrics);
    }

    internal static (VectorRecord[] Records, PackedAnnState State) Build(TestDatabase database,
        DistanceMetric metric)
    {
        var records = PackedAnnTestData.Load(database, metric);
        var state = PackedAnnBuilder.Build(PackedAnnTestData.Space(metric, Dimension), records,
            new PackedAnnOptions(), Budget(database));
        return (records, state);
    }

    internal static (VectorRecord[] Records, PackedAnnState State) BuildZeroDotProduct(TestDatabase database)
    {
        var space = PackedAnnTestData.Space(DistanceMetric.DotProduct, Dimension);
        var values = Enumerable.Range(0, RecordCount).Select(_ => new float[Dimension]).ToArray();
        PackedAnnIndexTestSupport.PersistVectors(database, space,
            PackedAnnTestData.Field(DistanceMetric.DotProduct), values);
        var records = PackedAnnTestData.Load(database, DistanceMetric.DotProduct);
        var state = PackedAnnBuilder.Build(space, records, new PackedAnnOptions(), Budget(database));
        return (records, state);
    }

    internal static AnnWorkBudget Budget(TestDatabase database)
        => new(new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits)), WorkLimit);
}
