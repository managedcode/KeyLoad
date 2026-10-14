using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class SubscriptionIndependentCheckpointR52Repair
{
    internal static async Task RunAsync(SubscriptionIndependentCheckpointR52State state,
        KeyLoadClient sdk, McpOfficialClient mcp, KeyLoadClient admin, McpOfficialClient official, CancellationToken token)
    {
        await PauseAsync(state, true, sdk, mcp, admin, official, token);
        var definition = state.IndependentDefinition with
        { EventTypes = [SubscriptionFilterRf3Protocol.Created, SubscriptionFilterRf3Protocol.Changed] };
        var request = new ConfigureSubscriptionRequest(Guid.NewGuid(), state.Seed.Independent, definition,
            ExpectedGeneration: 1);
        var result = await McpCallerAssertions.SdkSuccessAsync(await sdk.ConfigureSubscriptionAsync(request, token));
        state.Generation = 2;
        state.Issued = state.Checkpoint;
        state.IndependentDefinition = definition;
        await SubscriptionFilterRf3Assertions.EqualAsync(result,
            SubscriptionIndependentCheckpointR52Current.Expected(state, state.Seed.Independent));
        state.ConfigureHistory.Add((request, result));
        await SubscriptionIndependentCheckpointR52Current.RunAsync(state, sdk, mcp, admin, official, token);
        await SubscriptionFilterRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<SubscriptionInfo>(
            await mcp.CallAsync(McpCallerTools.SubscriptionsConfigure, request, token))).Value, result);
        await SubscriptionIndependentCheckpointR52Current.RunAsync(state, sdk, mcp, admin, official, token);
        await PauseAsync(state, false, sdk, mcp, admin, official, token);
    }

    private static async Task PauseAsync(SubscriptionIndependentCheckpointR52State state, bool paused,
        KeyLoadClient sdk, McpOfficialClient mcp, KeyLoadClient admin, McpOfficialClient official, CancellationToken token)
    {
        var request = new SetSubscriptionPausedRequest(Guid.NewGuid(), state.Seed.Independent, state.Generation, paused);
        var result = await McpCallerAssertions.SdkSuccessAsync(await sdk.SetSubscriptionPausedAsync(request, token));
        state.Paused = paused;
        await SubscriptionFilterRf3Assertions.EqualAsync(result,
            SubscriptionIndependentCheckpointR52Current.Expected(state, state.Seed.Independent));
        state.PauseHistory.Add((request, result));
        await SubscriptionIndependentCheckpointR52Current.RunAsync(state, sdk, mcp, admin, official, token);
        await SubscriptionFilterRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<SubscriptionInfo>(
            await mcp.CallAsync(McpCallerTools.SubscriptionsPause, request, token))).Value, result);
        await SubscriptionIndependentCheckpointR52Current.RunAsync(state, sdk, mcp, admin, official, token);
    }

    internal static async Task ReplayAsync(SubscriptionIndependentCheckpointR52State state,
        KeyLoadClient sdk, McpOfficialClient mcp, KeyLoadClient admin, McpOfficialClient official, CancellationToken token)
    {
        foreach (var saved in state.PauseHistory)
        {
            await SubscriptionFilterRf3Assertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
                await sdk.SetSubscriptionPausedAsync(saved.Request, token)), saved.Result);
            await SubscriptionIndependentCheckpointR52Current.RunAsync(state, sdk, mcp, admin, official, token);
            await SubscriptionFilterRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<SubscriptionInfo>(
                await mcp.CallAsync(McpCallerTools.SubscriptionsPause, saved.Request, token))).Value, saved.Result);
            await SubscriptionIndependentCheckpointR52Current.RunAsync(state, sdk, mcp, admin, official, token);
        }
        foreach (var saved in state.ConfigureHistory)
        {
            await SubscriptionFilterRf3Assertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
                await sdk.ConfigureSubscriptionAsync(saved.Request, token)), saved.Result);
            await SubscriptionIndependentCheckpointR52Current.RunAsync(state, sdk, mcp, admin, official, token);
            await SubscriptionFilterRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<SubscriptionInfo>(
                await mcp.CallAsync(McpCallerTools.SubscriptionsConfigure, saved.Request, token))).Value, saved.Result);
            await SubscriptionIndependentCheckpointR52Current.RunAsync(state, sdk, mcp, admin, official, token);
        }
    }
}
