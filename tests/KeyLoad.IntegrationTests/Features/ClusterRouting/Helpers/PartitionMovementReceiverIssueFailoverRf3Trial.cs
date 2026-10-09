using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>The original parent owns both callback holds and every faulted receiver lifecycle.</summary>
internal static class PartitionMovementReceiverIssueFailoverRf3Trial
{
    internal static async Task RunAsync(bool allReceivers, CancellationToken cancellationToken)
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
            var terminal = await PartitionMovementReceiverIssueFailoverRf3Producer.ExecuteAsync(wave, seed,
                allReceivers, caller.Token).ConfigureAwait(false);
            if (allReceivers)
            {
                await PartitionMovementReceiverIssueFailoverRf3LostObservation.RequireAsync(wave, seed, terminal,
                caller.Token).ConfigureAwait(false);
            }
            else
            {
                await PartitionMovementReceiverIssueFailoverRf3Healthy.RequireAsync(wave, seed, terminal,
                caller.Token).ConfigureAwait(false);
            }
        }, failures).ConfigureAwait(false);
        if (seed is { } clients)
        { await ServerFailureObserver.ObserveAsync(() => clients.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        if (wave is { } resources)
        { await ServerFailureObserver.ObserveAsync(() => resources.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
