using KeyLoad.Core.Search;
using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Server.Search;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.Search;

internal static class AnnPinnedLifetimeAssertions
{
    private const int Count = 3;
    private static readonly string[] LiteralIds = ["seed-00002", "seed-00001", "seed-00000"];
    private static readonly float[][] LiteralVectors = [[2.25f, 1f, -0.5f], [1.25f, 1f, -0.5f], [0.25f, 1f, -0.5f]];
    private static readonly long[] LiteralRevisions = [1, 1, 1];
    private static readonly float[] Query = [1, 0, 0];

    internal static async Task AssertAsync(AnnGenerationOwner owner,
        AnnMaintenanceRequest request, AnnSeed seed)
    {
        AnnIndexLease? lease = null;
        AnnIndexLease? concurrent = null;
        Task? disposal = null;
        var failures = new List<Exception>();
        try
        {
            lease = owner.Acquire(request, seed);
            concurrent = owner.Acquire(request, seed);
            var actual = lease;
            var second = concurrent;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await AssertLiteralSearchAsync(second);
                disposal = owner.DisposeAsync().AsTask();
                await Assert.That(disposal.IsCompleted).IsFalse();
                _ = Assert.ThrowsExactly<ObjectDisposedException>(() => AcquireUnexpected(owner, request, seed));
                second.Dispose();
                concurrent = null;
                await Assert.That(disposal.IsCompleted).IsFalse();
                await AssertLiteralSearchAsync(actual);
            }, failures);
        }
        finally
        {
            if (concurrent is not null)
            {
                ServerFailureObserver.Observe(() => concurrent.Dispose(), failures);
            }
            if (lease is not null)
            {
                ServerFailureObserver.Observe(() => lease.Dispose(), failures);
            }
            await ServerFailureObserver.ObserveAsync(() => disposal ?? owner.DisposeAsync().AsTask(), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task AssertLiteralSearchAsync(AnnIndexLease actual)
    {
        var result = actual.Index.Search(Query, Count, null, PackedAnnIndexTestSupport.Budget());
        await Assert.That(result.Candidates.Select(row => row.DocumentId).ToArray()).IsEquivalentTo(LiteralIds, CollectionOrdering.Matching);
        for (var index = 0; index < Count; index++)
        {
            await Assert.That(result.Candidates[index].Score).IsEqualTo(SearchEngine.Similarity(Query, LiteralVectors[index], DistanceMetric.Cosine));
        }
        await Assert.That(result.Candidates.Select(row => row.DocumentRevision).ToArray()).IsEquivalentTo(LiteralRevisions, CollectionOrdering.Matching);
    }
    private static void AcquireUnexpected(AnnGenerationOwner owner, AnnMaintenanceRequest request, AnnSeed seed)
    {
        using var lease = owner.Acquire(request, seed);
        throw new InvalidOperationException("The execution ANN reader admission unexpectedly succeeded.");
    }
}
