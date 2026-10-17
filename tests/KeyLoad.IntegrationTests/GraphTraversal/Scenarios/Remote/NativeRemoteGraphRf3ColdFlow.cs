using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class NativeRemoteGraphRf3ColdFlow
{
    internal static async Task RunAsync(TwoRf3MembershipWave wave, NativeRemoteGraphRf3Seed seed,
        NativeRemoteGraphRf3Operations originals, CancellationToken token)
    {
        await NativeRemoteGraphRf3Assertions.DocumentsAsync(wave, seed, token);
        var before = await NativeRemoteGraphRf3Cuts.CaptureAsync(wave, seed.Directory, token);
        await wave.RestartJoinedAsync(token);
        _ = await originals.ReplayAsync(wave, seed, token);
        await NativeRemoteGraphRf3Assertions.DocumentsAsync(wave, seed, token);
        var first = await NativeRemoteGraphRf3Cuts.CaptureAsync(wave, seed.Directory, token);
        await NativeRemoteGraphRf3ColdAssertions.RequireUnchangedAsync(before, first, seed, originals, token);
        await wave.RestartJoinedAsync(token);
        _ = await originals.ReplayAsync(wave, seed, token);
        await NativeRemoteGraphRf3Assertions.DocumentsAsync(wave, seed, token);
        var second = await NativeRemoteGraphRf3Cuts.CaptureAsync(wave, seed.Directory, token);
        await NativeRemoteGraphRf3ColdAssertions.RequireUnchangedAsync(first, second, seed, originals, token);
    }
}
