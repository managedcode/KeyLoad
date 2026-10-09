using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Original six Aspire owners and original clients surround active control failure and cold healthy continuation.</summary>
internal static class PartitionMovementActiveAdjunctRf3Trial
{
    internal static async Task RunAsync(bool duplicate, CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        TwoRf3MembershipWave? wave = null;
        PartitionMovementPublicParentRf3Seed? seed = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            wave = await TwoRf3MembershipWave.StartProtectedDocumentsAsync(caller.Token).ConfigureAwait(false);
            seed = await PartitionMovementPublicParentRf3Seed.CreateAsync(wave, caller.Token).ConfigureAwait(false);
            await ExecuteAsync(wave, seed, duplicate, caller.Token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (seed is { } clients)
        { await ServerFailureObserver.ObserveAsync(() => clients.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        if (wave is { } owners)
        { await ServerFailureObserver.ObserveAsync(() => owners.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        bool duplicate, CancellationToken cancellationToken)
    {
        var initial = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
            cancellationToken).ConfigureAwait(false);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken).ConfigureAwait(false);
        await PartitionMovementActiveAdjunctRf3Producer.ExecuteAsync(wave, seed, duplicate,
            cancellationToken).ConfigureAwait(false);
        var failed = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
            PartitionMovementReceiverIssueFailoverRf3NativeCut.StageId(seed), cancellationToken).ConfigureAwait(false);
        await PartitionMovementActiveAdjunctRf3Assertions.RequireBusinessUnchangedAsync(seed, initial, failed);
        await PartitionMovementActiveAdjunctRf3Healthy.RequireAsync(wave, seed, failed,
            cancellationToken).ConfigureAwait(false);
    }
}
