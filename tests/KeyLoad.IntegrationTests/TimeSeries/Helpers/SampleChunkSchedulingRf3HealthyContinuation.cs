using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkSchedulingRf3HealthyContinuation
{
    internal static async Task ExecuteAsync(TwoRf3MembershipWave wave,
        SampleChunkJobRevocationScenario original, CancellationToken token)
    {
        SampleChunkJobRevocationScenario fresh;
        await using (var administrator = await RequestCqrsRf3Callers.ConnectAsync(wave.Application,
            RequestCqrsRf3Protocol.Node1, wave.Profile.AdminKey, token).ConfigureAwait(false))
        {
            fresh = await SampleChunkJobRevocationScenario.CreateAsync(administrator.Sdk, token).ConfigureAwait(false);
            await using var creator = await RequestCqrsRf3Callers.ConnectAsync(wave.Application,
                RequestCqrsRf3Protocol.Node2, fresh.Secret, token).ConfigureAwait(false);
            await fresh.SeedAsync(creator.Sdk, administrator.Sdk, token).ConfigureAwait(false);
            fresh.Correction = await fresh.Window.CommitAsync(creator.Sdk,
                new AppendSamples(TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series,
                    [fresh.Window.Late], TimeSeriesRf3Scenario.PrivateTags), token).ConfigureAwait(false);
            var merged = await fresh.Window.WaitForActualMergeAsync(creator.Sdk, token).ConfigureAwait(false);
            await SampleChunkRf3Assertions.CompleteAsync(fresh.Window, merged, fresh.Correction).ConfigureAwait(false);
            await SampleChunkJobRevocationAssertions.HealthyAsync(fresh, creator.Sdk, creator.Mcp, token).ConfigureAwait(false);
        }
        _ = await wave.StopForDirectoryReadAsync().ConfigureAwait(false);
        await wave.RestartJoinedAsync(token).ConfigureAwait(false);
        await using var originalCaller = await RequestCqrsRf3Callers.ConnectAsync(wave.Application,
            RequestCqrsRf3Protocol.Node1, original.Secret, token).ConfigureAwait(false);
        await SampleChunkJobRevocationAssertions.HealthyAsync(original, originalCaller.Sdk, originalCaller.Mcp, token)
            .ConfigureAwait(false);
        await using var freshCaller = await RequestCqrsRf3Callers.ConnectAsync(wave.Application,
            RequestCqrsRf3Protocol.Node2, fresh.Secret, token).ConfigureAwait(false);
        await SampleChunkJobRevocationAssertions.HealthyAsync(fresh, freshCaller.Sdk, freshCaller.Mcp, token)
            .ConfigureAwait(false);
    }
}
