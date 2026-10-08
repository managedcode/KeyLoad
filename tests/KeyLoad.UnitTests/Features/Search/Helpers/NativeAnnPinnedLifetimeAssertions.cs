using KeyLoad.Core.Features.Search;
using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Server.Features.Search;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeAnnPinnedLifetimeAssertions
{
    private const int Count = 3;
    private static readonly string[] LiteralIds = ["seed-00002", "seed-00001", "seed-00000"];
    private static readonly float[][] LiteralVectors = [[2.25f, 1f, -0.5f], [1.25f, 1f, -0.5f], [0.25f, 1f, -0.5f]];
    private static readonly long[] LiteralRevisions = [1, 1, 1];
    private static readonly float[] Query = [1, 0, 0];

    internal static async Task AssertAsync(TestDatabase database, NativeAnnGenerationOwner owner,
        AnnMaintenanceRequest request, AnnSeed seed)
    {
        NativeAnnIndexLease? lease = null;
        Task? disposal = null;
        var failures = new List<Exception>();
        try
        {
            lease = owner.Acquire(request, seed);
            var actual = lease;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var exhausted = Assert.ThrowsExactly<KeyLoadException>(() => AcquireUnexpected(owner, request, seed));
                await Assert.That(exhausted.Code).IsEqualTo(ErrorCode.ResourceExhausted);
                disposal = owner.DisposeAsync().AsTask();
                await Assert.That(disposal.IsCompleted).IsFalse();
                _ = Assert.ThrowsExactly<ObjectDisposedException>(() => AcquireUnexpected(owner, request, seed));
                await AssertLiteralSearchAsync(database, actual);
            }, failures);
        }
        finally
        {
            if (lease is not null)
            { ServerFailureObserver.Observe(() => lease.Dispose(), failures); }
            await ServerFailureObserver.ObserveAsync(() => disposal ?? owner.DisposeAsync().AsTask(), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task AssertLiteralSearchAsync(TestDatabase database, NativeAnnIndexLease actual)
    {
        var result = actual.Index.Search(Query, Count, null, PackedAnnIndexTestSupport.Budget(database));
        await Assert.That(result.Candidates.Select(row => row.DocumentId).ToArray()).IsEquivalentTo(LiteralIds, CollectionOrdering.Matching);
        for (var index = 0; index < Count; index++)
        { await Assert.That(result.Candidates[index].Score).IsEqualTo(SearchEngine.Similarity(Query, LiteralVectors[index], DistanceMetric.Cosine)); }
        await Assert.That(result.Candidates.Select(row => row.DocumentRevision).ToArray()).IsEquivalentTo(LiteralRevisions, CollectionOrdering.Matching);
    }
    private static void AcquireUnexpected(NativeAnnGenerationOwner owner, AnnMaintenanceRequest request, AnnSeed seed)
    {
        using var lease = owner.Acquire(request, seed);
        throw new InvalidOperationException("The native ANN reader admission unexpectedly succeeded.");
    }
}
