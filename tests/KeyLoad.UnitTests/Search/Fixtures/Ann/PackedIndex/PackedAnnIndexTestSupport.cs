using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal static class PackedAnnIndexTestSupport
{
    internal const long GenerousWorkLimit = 1_000_000_000;

    internal static AnnWorkBudget Budget(TestDatabase database, long maxWorkUnits = GenerousWorkLimit,
        CancellationToken token = default)
        => new(new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits), cancellationToken: token), maxWorkUnits);

    internal static PackedAnnIndex Build(TestDatabase database, DistanceMetric metric, VectorRecord[] records,
        PackedAnnOptions? options = null, AnnWorkBudget? budget = null)
        => PackedAnnIndex.Build(PackedAnnTestData.Space(metric, records.FirstOrDefault()?.Values.Length ?? 1), records,
            UnitExecutionOptions.PackedAnn(options ?? new()), budget ?? Budget(database));

    internal static PackedAnnIndex Build(VectorSpace space, IReadOnlyList<VectorRecord> records,
        PackedAnnOptions options, AnnWorkBudget budget)
        => PackedAnnIndex.Build(space, records, UnitExecutionOptions.PackedAnn(options), budget);

    internal static void PersistVectors(TestDatabase database, VectorSpace space, string field,
        IReadOnlyList<float[]> vectors)
    {
        for (var start = 0; start < vectors.Count; start += PackedAnnTestData.SeedBatchSize)
        {
            var end = Math.Min(vectors.Count, start + PackedAnnTestData.SeedBatchSize);
            var mutations = new List<Mutation>((end - start) * 2);
            for (var index = start; index < end; index++)
            {
                var id = PackedAnnTestData.DocumentId(index);
                mutations.Add(new PutDocument(PackedAnnTestData.Collection, id, "{}"));
                mutations.Add(new PutVector(PackedAnnTestData.Collection, id, field,
                    ImmutableArray.CreateRange(vectors[index]), space, PackedAnnTestData.DocumentRevision));
            }
            database.Commit([.. mutations]);
        }
    }
}
