using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal static class DropTableLifecycleRf3Flow
{
    private const int ColdWaves = 2;

    internal static async Task RunAsync(CancellationToken token)
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
        TwoRf3MembershipWave? wave = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            wave = await TwoRf3MembershipWave.StartProtectedDocumentsAsync(caller.Token).ConfigureAwait(false);
            var state = await DropTableLifecycleRf3Warm.RunAsync(wave, caller.Token).ConfigureAwait(false);
            for (var cold = 0; cold < ColdWaves; cold++)
            {
                var proofs = await DropTableLifecycleRf3Cold.ReopenAsync(wave, state, caller.Token).ConfigureAwait(false);
                await DropTableLifecycleRf3Replay.VerifyAsync(wave, state, proofs, caller.Token).ConfigureAwait(false);
            }
        }, failures).ConfigureAwait(false);
        if (wave is { } resources)
        {
            if (failures.Count > 0)
            {
                resources.RetainRoots();
            }
            await ServerFailureObserver.ObserveAsync(() => resources.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
