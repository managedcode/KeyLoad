using KeyLoad.Query;
using KeyLoad.Query.Search;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Search;

internal static class AnnRetiredReaderAssertions
{
    private const int Count = 3;
    private const long Revision = 1;
    private const string Field = "/embedding";
    private static readonly string[] Ids = ["seed-00000", "seed-00001", "seed-00002"];
    private static readonly float[][] Values = [[0, 1, 0], [1, 1, 0], [2, 1, 0]];
    private static readonly int[] Order = [2, 1, 0];
    private static readonly float[] Query = [1, 0, 0];
    private static readonly ulong[] Eligible = [7];
    private static readonly VectorSpace Space = new("seed-space", Count, DistanceMetric.Cosine, "seed-model", "v1");

    internal static async Task AssertAsync(TestDatabase database, IAnnProjectionLease held,
        AnnMaintenanceRequest pin, CommitReceipt receipt)
    {
        var before = AnnMaintenanceTestData.Snapshot(database);
        var position = database.Store.Position;
        await Assert.That(held.IndexGeneration).IsEqualTo(pin.IndexGeneration);
        await Assert.That(held.IndexedAppliedPosition).IsGreaterThanOrEqualTo(receipt.Token.Position);
        await Assert.That(held.Records.Length).IsEqualTo(Count);
        for (var index = 0; index < Count; index++)
        {
            var expected = new VectorRecord(Ids[index], Field, Space, [.. Values[index]], Revision);
            await Assert.That(OrleansSerialization.Serialize(held.Records[index]).AsSpan()
                .SequenceEqual(OrleansSerialization.Serialize(expected))).IsTrue();
        }
        var read = new global::KeyLoad.Core.ReadExecutionBudget(database.Database.OperationLimitsOptions,
            database.Database.EvaluationClock, TestContext.Current!.Execution.CancellationToken);
        var actual = held.Search(Query, Count, Eligible, new AnnWorkBudget(read, held.CallerSeedOptions.Value.MaxWorkUnits));
        await Assert.That(actual.Mode).IsEqualTo(AnnSearchMode.ExactSmallSet);
        await Assert.That(actual.EligibleCount).IsEqualTo(Count);
        await Assert.That(actual.Candidates.Length).IsEqualTo(Count);
        for (var index = 0; index < Count; index++)
        {
            var ordinal = Order[index];
            var candidate = actual.Candidates[index];
            await Assert.That(candidate.SourceOrdinal).IsEqualTo(ordinal);
            await Assert.That(candidate.DocumentId).IsEqualTo(Ids[ordinal]);
            await Assert.That(candidate.DocumentRevision).IsEqualTo(Revision);
            await Assert.That(candidate.Score).IsEqualTo(SearchEngine.Similarity(Query, Values[ordinal], DistanceMetric.Cosine));
        }
        await Assert.That(AnnMaintenanceTestData.Snapshot(database)).IsEquivalentTo(before, CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(position);
    }
}
