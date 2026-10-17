using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PhysicalShardAuthorityBoundaryRf3Flow
{
    private const int FirstEntry = 0;
    private const long FirstPublication = 1;
    private const long NoGrants = 0;
    internal const int NoFailures = 0;
    internal static async Task RunAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed[] seeds,
        PhysicalShardAuthorityBoundaryRf3Actor actor, KeyLoadClient sourceRoot, KeyLoadClient targetRoot, CancellationToken token)
    {
        var requests = seeds.Select(seed => seed.FirstRequest).ToArray();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var iterator = actor.Sdk.MoveWholePartitionsAsync(requests, token)
                .GetAsyncEnumerator(CancellationToken.None);
            await ServerFailureObserver.ObserveAsync(() => RunIteratorAsync(wave, seeds, requests, actor, sourceRoot,
                targetRoot, iterator, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RunIteratorAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed[] seeds,
        PartitionMoveRequest[] requests, PhysicalShardAuthorityBoundaryRf3Actor actor, KeyLoadClient sourceRoot, KeyLoadClient targetRoot,
        IAsyncEnumerator<ManagedCode.Communication.Result<PartitionMoveResult>> iterator, CancellationToken token)
    {
        await Assert.That(await ObserveOriginalAsync(iterator.MoveNextAsync().AsTask())).IsTrue();
        var first = await McpCallerAssertions.SdkSuccessAsync(iterator.Current);
        await PhysicalShardPartitionMovementRf3Placement.RequireAsync(seeds[FirstEntry], requests[FirstEntry],
            seeds[FirstEntry].OriginalPlacement, seeds[FirstEntry].Directory.ControlOwner, first,
            checked(seeds[FirstEntry].OriginalPlacement.DirectoryRevision + FirstPublication),
            checked(seeds[FirstEntry].OriginalPlacement.DirectoryRevision + FirstPublication), token);
        await PhysicalShardPartitionMovementRf3Flow.VerifyAsync(seeds, token);
        var baseline = await PhysicalShardAuthorityBoundaryRf3Cut.CaptureAndReopenAsync(wave, seeds, token);
        foreach (var source in baseline[FirstEntry].Take(TwoRf3MembershipProtocol.MembersPerGroup))
        {
            await Assert.That(source.Header!.OperatorPrincipalId).IsEqualTo(PhysicalShardAuthorityBoundaryRf3Actor.PrincipalId);
            await Assert.That(source.OutstandingPrincipalGrants).IsEqualTo((long?)NoGrants);
            await Assert.That(source.OutstandingMoveGrants).IsEqualTo(NoGrants);
        }
        await PhysicalShardAuthorityBoundaryRf3Policy.ChangeAsync(wave, seeds, actor, sourceRoot, targetRoot, baseline,
            PhysicalShardAuthorityBoundaryRf3Policy.RevokedEpoch, false, token);
        await Assert.That(await ObserveOriginalAsync(iterator.MoveNextAsync().AsTask())).IsTrue();
        await PhysicalShardAuthorityBoundaryRf3Policy.DeniedAsync(iterator.Current);
        await PhysicalShardAuthorityBoundaryRf3Cut.RequireAndReopenAsync(wave, seeds, baseline, token);
        await Assert.That(await ObserveOriginalAsync(iterator.MoveNextAsync().AsTask())).IsFalse();
        await actor.ReconnectAsync(wave, token);
        await PhysicalShardAuthorityBoundaryRf3Policy.DeniedAsync(await ObserveOriginalAsync(
            actor.Sdk.MovePartitionAsync(requests[FirstEntry], token)));
        await PhysicalShardAuthorityBoundaryRf3Cut.RequireAndReopenAsync(wave, seeds, baseline, token);
        await PhysicalShardAuthorityBoundaryRf3Policy.ChangeAsync(wave, seeds, actor, sourceRoot, targetRoot, baseline,
            PhysicalShardAuthorityBoundaryRf3Policy.RepairedEpoch, true, token);
        await PhysicalShardAuthorityBoundaryRf3Healthy.RunAsync(wave, seeds, actor, requests, first, token);
    }
    internal static async Task<T> ObserveOriginalAsync<T>(Task<T> original)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => original, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return await original.ConfigureAwait(false);
    }

}
