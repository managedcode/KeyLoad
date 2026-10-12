using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class NativeRemoteGraphRf3Trial
{
    internal static async Task RunAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var principal = "c1-probe-remote-graph-" + Guid.NewGuid().ToString("N");
        var failures = new List<Exception>();
        global::KeyLoad.IntegrationTests.Features.ClusterReplication.LocalRf3ImageTestSession? localImage = null;
        TwoRf3MembershipWave? wave = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            localImage = await global::KeyLoad.IntegrationTests.Features.ClusterReplication.LocalRf3ImageTestSession
                .StartIfSelectedAsync(token).ConfigureAwait(false);
            var startedWave = await NativeCanonicalModelCutWaveStartup.StartAsync(principal, token, localImage?.Selection);
            wave = startedWave;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var seed = await NativeRemoteGraphRf3Seed.CreateAsync(startedWave, principal, token);
                await NativeRemoteGraphRf3AuthorizationFlow.RunAsync(startedWave, seed, token);
                var originals = await NativeRemoteGraphRf3Operations.ExecuteAsync(startedWave, seed, token);
                await NativeRemoteGraphRf3ColdFlow.RunAsync(startedWave, seed, originals, token);
            }, failures);
        }, failures);
        var beforeWaveCleanup = failures.Count;
        if (wave is { } ownedWave)
        { await ServerFailureObserver.ObserveAsync(() => ownedWave.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        var waveCleanupSucceeded = wave is not null && failures.Count == beforeWaveCleanup;
        if (localImage is { } ownedImage)
        { await ServerFailureObserver.ObserveAsync(() => ownedImage.DisposeAsync(removeImage: waveCleanupSucceeded).AsTask(), failures).ConfigureAwait(false); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
