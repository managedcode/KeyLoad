using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PhysicalShardNonRetiredPlanRf3Assertions
{
    private const int FirstEntry = 0;
    private const int SecondEntry = 1;
    internal static async Task RequireAsync(PartitionMovementPublicParentRf3Seed[] seeds,
        PartitionMoveRequest[] requests, PartitionMoveResult expected, CancellationToken token)
    {
        await Assert.That(expected.Phase).IsEqualTo(PartitionMovePhase.Aborted);
        PartitionMoveRequest[] plan = [requests[FirstEntry] with
        {
            Mode = PartitionMoveMode.Resume
        }, requests[SecondEntry]];
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var iterator = seeds[FirstEntry].Source.MoveWholePartitionsAsync(plan, token)
                .GetAsyncEnumerator(CancellationToken.None);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await Assert.That(await ReadOriginalAsync(iterator.MoveNextAsync().AsTask())).IsTrue();
                await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(iterator.Current));
                await PartitionMovementFencedAbortRf3Assertions.UnchangedAsync(seeds, token);
                await Assert.That(await ReadOriginalAsync(iterator.MoveNextAsync().AsTask())).IsFalse();
                await PartitionMovementFencedAbortRf3Assertions.UnchangedAsync(seeds, token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task<bool> ReadOriginalAsync(Task<bool> original)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => original, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return await original.ConfigureAwait(false);
    }
}
