using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>One genuine six-owner wave keeps failed Close ahead of Abort and every resource stop.</summary>
internal static class PartitionMovementTransferCloseRf3Trial
{
    internal static async Task RunAsync(CancellationToken cancellationToken)
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
            await ExecuteAsync(wave, seed, caller.Token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (seed is { } clients)
        { await ServerFailureObserver.ObserveAsync(() => clients.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        if (wave is { } resources)
        { await ServerFailureObserver.ObserveAsync(() => resources.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        CancellationToken cancellationToken)
    {
        var before = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
            cancellationToken).ConfigureAwait(false);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken).ConfigureAwait(false);
        await PartitionMovementTransferCloseRf3Producer.CancelAfterActualPageAsync(wave, seed,
            cancellationToken).ConfigureAwait(false);
        // No owner is stopped between actual Close failure and this fresh authorized Abort.
        var terminal = await PartitionMovementTransferCloseRf3AbortAssertions.AbortAsync(seed,
            cancellationToken).ConfigureAwait(false);
        var closed = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
            PartitionMovementTransferCloseRf3AbortAssertions.SourceClosureId(seed.FirstRequest),
            cancellationToken).ConfigureAwait(false);
        await PartitionMovementTransferCloseRf3NativeCut.RequireJoinedAsync(wave, seed, before, closed, terminal);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken).ConfigureAwait(false);
        await PartitionMovementTransferCloseRf3Healthy.RequireAsync(wave, seed, terminal,
            cancellationToken).ConfigureAwait(false);
    }
}
