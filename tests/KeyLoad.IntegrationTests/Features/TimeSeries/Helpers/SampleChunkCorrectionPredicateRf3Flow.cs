using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkCorrectionPredicateRf3Flow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var wave = await TwoRf3MembershipWave.StartProtectedDocumentsAsync(token);
            await wave.WaitForSixHealthyAsync(token);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var state = await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application,
                    RequestCqrsRf3Protocol.Node1, wave.Profile.AdminKey, administrator =>
                    SeedAsync(wave, administrator, token), token);
                for (var cold = 0; cold < SampleChunkCorrectionPredicateRf3State.ColdCycles; cold++)
                {
                    _ = await wave.StopForDirectoryReadAsync();
                    await wave.RestartJoinedAsync(token);
                    await CurrentAsync(wave, state, token);
                }
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task<SampleChunkCorrectionPredicateRf3State> SeedAsync(TwoRf3MembershipWave wave,
        RequestCqrsRf3Callers administrator, CancellationToken token)
    {
        var scenario = await SampleChunkJobRevocationScenario.CreateAsync(administrator.Sdk, token);
        return await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, RequestCqrsRf3Protocol.Node2,
            scenario.Secret, async creator =>
            {
                await scenario.SeedAsync(creator.Sdk, administrator.Sdk, token);
                return await SampleChunkCorrectionPredicateRf3Held.RunAsync(wave, scenario, administrator, creator, token);
            }, token);
    }

    private static Task<bool> CurrentAsync(TwoRf3MembershipWave wave,
        SampleChunkCorrectionPredicateRf3State state, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, RequestCqrsRf3Protocol.Node1,
            wave.Profile.AdminKey, administrator => RequestCqrsRf3Callers.RunOwnedAsync(wave.Application,
                RequestCqrsRf3Protocol.Node2, state.Scenario.Secret, async creator =>
                {
                    await SampleChunkCorrectionPredicateRf3Reads.WaveAsync(administrator, creator, state, 2L, 6L, token);
                    return true;
                }, token), token);
}
