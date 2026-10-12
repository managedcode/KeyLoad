using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class PhysicalShardPartitionMovementRf3Tests
{
    private const int FirstSeed = 0;

    [Test]
    public async Task OrderedWholePartitionsResumeOriginalReceiptsAfterPartialCancellationThenMergeCold()
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken,
            deadline.Token);
        var failures = new List<Exception>();
        TwoRf3MembershipWave? wave = null;
        var seeds = new List<PartitionMovementPublicParentRf3Seed>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            wave = await TwoRf3MembershipWave.StartProtectedDocumentsAsync(caller.Token).ConfigureAwait(false);
            seeds.Add(await PartitionMovementPublicParentRf3Seed.CreateAsync(wave, caller.Token).ConfigureAwait(false));
            seeds.Add(await PartitionMovementPublicParentRf3Seed.CreateAsync(wave, seeds[FirstSeed].Credential,
                caller.Token).ConfigureAwait(false));
            await PhysicalShardPartitionMovementRf3Flow.RunAsync(wave, seeds.ToArray(), caller.Token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        foreach (var seed in seeds)
        { await ServerFailureObserver.ObserveAsync(() => seed.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        if (wave is { } resources)
        { await ServerFailureObserver.ObserveAsync(() => resources.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
