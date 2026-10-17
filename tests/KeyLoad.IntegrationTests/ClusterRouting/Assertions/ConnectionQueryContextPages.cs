using KeyLoad.Client;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class ConnectionQueryContextPages
{
    internal static async Task<ConnectionQueryContextRead> SdkAsync(ConnectionRf3Scenario scenario, ConnectionQueryContextState state,
        KeyLoadClient sdk, ConnectionQueryContextSeed seed, ConnectionProbeWitness? owner, CancellationToken token)
    {
        var reads = new List<ConnectionQueryContextRead>();
        for (var route = 0; route < 3; route++)
        {
            var first = await ConnectionQueryContextCurrent.CaptureAsync(scenario, state, async () =>
            {
                var actual = await ConnectionQueryContextCalls.SuccessAsync(scenario, sdk, seed.Identity, route, null, token);
                await ConnectionQueryContextCurrent.PageAsync(actual.Page, seed, true, null);
                if (owner is not null)
                {
                    await ConnectionRf3WitnessReader.SameOwnerAsync(owner, actual.Witness);
                }
                return actual;
            }, token);
            reads.Add(first);
            await ConnectionQueryContextCurrent.ObserveAsync(scenario, state, async () =>
            {
                var following = await ConnectionQueryContextCalls.SuccessAsync(scenario, sdk, seed.Identity,
                    (route + 1) % 3, first.Page.Cursor, token);
                await ConnectionQueryContextCurrent.PageAsync(following.Page, seed, false, null, first.Page.CutPosition);
                await ConnectionRf3WitnessReader.SameOwnerAsync(first.Witness, following.Witness);
            }, token);
        }
        return reads[0];
    }
    internal static async Task<ConnectionQueryContextRead> McpAsync(ConnectionRf3Scenario scenario, ConnectionQueryContextState state,
        ConnectionRf3Caller caller, CancellationToken token)
    {
        var reads = new List<ConnectionQueryContextRead>();
        for (var route = 0; route < 3; route++)
        {
            var first = await ConnectionQueryContextCurrent.CaptureAsync(scenario, state, async () =>
            {
                var actual = await ConnectionQueryContextCalls.McpAsync(scenario, caller, state.First.Identity, route, null, token);
                await ConnectionQueryContextCurrent.PageAsync(actual.Page, state.First, true, null);
                return actual;
            }, token);
            reads.Add(first);
            await ConnectionQueryContextCurrent.ObserveAsync(scenario, state, async () =>
            {
                var following = await ConnectionQueryContextCalls.McpAsync(scenario, caller, state.First.Identity,
                    (route + 1) % 3, first.Page.Cursor, token);
                await ConnectionQueryContextCurrent.PageAsync(following.Page, state.First, false, null, first.Page.CutPosition);
                await ConnectionRf3WitnessReader.SameOwnerAsync(first.Witness, following.Witness);
            }, token);
        }
        return reads[0];
    }
}
