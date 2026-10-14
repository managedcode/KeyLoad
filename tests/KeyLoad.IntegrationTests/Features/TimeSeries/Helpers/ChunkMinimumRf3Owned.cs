using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class ChunkMinimumRf3Owned
{
    internal static async Task RunAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        TwoRf3MembershipWave? wave = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            wave = await TwoRf3MembershipWave.StartProtectedDocumentsAsync(token);
            var state = await ChunkMinimumRf3Seed.RunAsync(wave, token);
            await ChunkMinimumRf3Flow.CurrentAsync(wave, state, token);
            await ChunkMinimumRf3Policy.RunAsync(wave, state, token);
            await ChunkMinimumRf3Cold.RunAsync(wave, state, token);
            await ChunkMinimumRf3Movement.RunAsync(wave, state, token);
            await ChunkMinimumRf3Flow.CurrentAsync(wave, state, token);
            await ChunkMinimumRf3Cold.RunAsync(wave, state, token);
        }, failures);
        if (wave is { } owned)
        {
            await ServerFailureObserver.ObserveAsync(() => owned.DisposeAsync().AsTask(), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
