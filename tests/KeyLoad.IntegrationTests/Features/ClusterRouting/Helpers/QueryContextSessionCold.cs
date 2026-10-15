using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class QueryContextSessionCold
{
    private const int FirstColdOrdinal = 1;
    private const int LastColdOrdinal = 2;
    private const int ColdOrdinalStep = 1;

    internal static async Task RunAsync(ConnectionRf3Scenario scenario, ConnectionQueryContextState state, List<Exception> failures,
        CancellationToken token)
    {
        for (var ordinal = FirstColdOrdinal; ordinal <= LastColdOrdinal; ordinal += ColdOrdinalStep)
        {
            await ServerFailureObserver.ObserveAsync(() => scenario.RestartQueryContextsColdAsync(ordinal, token), failures);
            await ServerFailureObserver.ObserveAsync(() => WaveAsync(scenario, state, token), failures);
            await ServerFailureObserver.ObserveAsync(() => scenario.RequireQueryColdReceiptsAsync(token), failures);
        }
        await ServerFailureObserver.ObserveAsync(() => scenario.RequireQueryColdReceiptsAsync(token), failures);
    }
    private static async Task WaveAsync(ConnectionRf3Scenario scenario, ConnectionQueryContextState state, CancellationToken token)
    {
        var caller = await scenario.CallerAsync(token, multiplexed: true);
        var first = await ConnectionQueryContextPages.SdkAsync(scenario, state, caller.Sdk, state.First, null, token);
        await QueryContextSessionRefusals.RunAsync(scenario, state, caller.WithCredential(state.Second.Identity.Secret), first.Witness, token);
        var fresh = await ConnectionQueryContextPages.SdkAsync(scenario, state, caller.Sdk, state.First, first.Witness, token);
        await scenario.CloseAsync(caller, fresh.Marker, token);
        var official = await scenario.CallerAsync(token);
        var second = await ConnectionQueryContextPages.SdkAsync(scenario, state, official.WithCredential(state.Second.Identity.Secret), state.Second, null, token);
        var result = await ConnectionQueryContextPages.McpAsync(scenario, state, official, token);
        await ConnectionRf3WitnessReader.SameOwnerAsync(second.Witness, result.Witness);
        await scenario.CloseAsync(official, result.Marker, token);
    }
}
