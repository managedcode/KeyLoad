using KeyLoad.Query;
using KeyLoad.Query.Features.Search;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal static class PackedAnnStressOracle
{
    private const long NoObservedWork = 0;
    internal static async Task VerifyAsync(PackedAnnIndex index, VectorRecord[] records, float[] query,
        TestDatabase database, CancellationToken token)
    {
        var bytes = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        var actual = index.Search(query, records.Length, null, PackedAnnIndexTestSupport.Budget(database, token: token));
        await ExactAsync(actual, records, query, index.Space.Metric);
        await Assert.That(index.RetainedBytesUpperBound).IsLessThanOrEqualTo(PackedAnnDeleteReinsertStress.Policy.MaxIndexBytes);
        await Assert.That(index.BuildScratchBytesUpperBound).IsLessThanOrEqualTo(PackedAnnDeleteReinsertStress.Policy.MaxScratchBytes);
        await Assert.That(actual.ScratchBytesUpperBound).IsLessThanOrEqualTo(PackedAnnDeleteReinsertStress.Policy.MaxScratchBytes);
        await UnchangedAsync(database.Store, bytes, position);
    }

    internal static async Task ExactAsync(AnnSearchResult actual, VectorRecord[] records, float[] query,
        DistanceMetric metric)
    {
        var expected = records.Select((row, ordinal) => new AnnCandidate(ordinal, row.DocumentId,
                row.DocumentRevision, SearchEngine.Similarity(query, row.Values.ToArray(), metric)))
            .OrderByDescending(row => row.Score).ThenBy(row => row.DocumentId, StringComparer.Ordinal).ToArray();
        await Assert.That(actual.Candidates).IsEquivalentTo(expected, CollectionOrdering.Matching);
        await Assert.That(actual.WorkUnits).IsGreaterThan(NoObservedWork);
        await Assert.That(actual.DistanceEvaluations).IsGreaterThan(NoObservedWork);
        await Assert.That(actual.EdgeVisits).IsGreaterThan(NoObservedWork);
    }

    internal static async Task UnchangedAsync(ZoneTreeStore store, string[] bytes, long position)
    {
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(store)).IsEquivalentTo(bytes, CollectionOrdering.Matching);
    }
}
