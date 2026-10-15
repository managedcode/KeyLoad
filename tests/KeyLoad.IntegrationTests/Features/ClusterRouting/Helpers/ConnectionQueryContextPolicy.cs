using KeyLoad.Client;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class ConnectionQueryContextPolicy
{
    private const string Denied = "The principal cannot perform this operation in this scope.";
    private const string Stale = "The query cursor no longer has a valid authorized read cut.";
    internal static async Task RunAsync(ConnectionRf3Scenario scenario, ConnectionQueryContextState state,
        ConnectionRf3Caller caller, KeyLoadClient second, ConnectionQueryContextRead first, CancellationToken token)
    {
        await ConnectionQueryContextCurrent.ObserveAsync(scenario, state, async () =>
        {
            var wrongSubject = await ConnectionQueryContextCalls.SdkAsync(scenario, second, state.Second.Identity,
                1, first.Page.Cursor, token);
            await ConnectionQueryContextCalls.ErrorAsync(wrongSubject.Result, ErrorCode.CursorExpired, Stale);
        }, token);
        await ConnectionQueryContextProvisioning.PolicyAsync(scenario, state.First.Identity, false, 3L, token);
        for (var route = 0; route < 3; route++)
        {
            await ConnectionQueryContextCurrent.ObserveAsync(scenario, state, async () =>
            {
                var refused = await ConnectionQueryContextCalls.SdkAsync(scenario, caller.Sdk, state.First.Identity, route, null, token);
                await ConnectionQueryContextCalls.ErrorAsync(refused.Result, ErrorCode.PermissionDenied, Denied);
                await ConnectionRf3WitnessReader.SameOwnerAsync(first.Witness, refused.Witness);
            }, token);
        }
        await ConnectionQueryContextPages.SdkAsync(scenario, state, second, state.Second, first.Witness, token);
        await ConnectionQueryContextProvisioning.PolicyAsync(scenario, state.First.Identity, true, 4L, token);
        await ConnectionQueryContextCurrent.ObserveAsync(scenario, state, async () =>
        {
            var stale = await ConnectionQueryContextCalls.SdkAsync(scenario, caller.Sdk, state.First.Identity, 0, first.Page.Cursor, token);
            await ConnectionQueryContextCalls.ErrorAsync(stale.Result, ErrorCode.CursorExpired, Stale);
        }, token);
        await ConnectionQueryContextPages.SdkAsync(scenario, state, caller.Sdk, state.First, first.Witness, token);
    }
}
