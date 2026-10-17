using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkJobRevocationFlow
{
    internal static async Task ExecuteAsync(TwoRf3MembershipWave wave, CancellationToken token)
    {
        SampleChunkJobRevocationScenario scenario;
        await using (var administrator = await RequestCqrsRf3Callers.ConnectAsync(wave.Application,
            RequestCqrsRf3Protocol.Node1, wave.Profile.AdminKey, token).ConfigureAwait(false))
        {
            scenario = await SampleChunkJobRevocationScenario.CreateAsync(administrator.Sdk, token).ConfigureAwait(false);
            await using var creator = await RequestCqrsRf3Callers.ConnectAsync(wave.Application,
                RequestCqrsRf3Protocol.Node2, scenario.Secret, token).ConfigureAwait(false);
            await scenario.SeedAsync(creator.Sdk, administrator.Sdk, token).ConfigureAwait(false);
            await SampleChunkJobRevocationHeld.ExecuteAsync(wave, scenario, administrator.Sdk, token).ConfigureAwait(false);
            await SampleChunkJobRevocationAssertions.HealthyAsync(scenario, creator.Sdk, creator.Mcp, token);
        }
        await wave.RestartJoinedAsync(token).ConfigureAwait(false);
        await using var cold = await RequestCqrsRf3Callers.ConnectAsync(wave.Application,
            RequestCqrsRf3Protocol.Node2, scenario.Secret, token).ConfigureAwait(false);
        await SampleChunkJobRevocationAssertions.HealthyAsync(scenario, cold.Sdk, cold.Mcp, token);
    }
}
