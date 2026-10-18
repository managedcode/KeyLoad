using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class SubscriptionIndependentCheckpointR52Replay
{
    internal static async Task RunAsync(SubscriptionIndependentCheckpointR52State state,
        KeyLoadClient sdk, McpOfficialClient mcp, KeyLoadClient admin, McpOfficialClient official, CancellationToken token)
    {
        await ProducerAsync(state, sdk, mcp, admin, official, token);
        await ReceiptsAsync(state, sdk, mcp, admin, official, token);
        await RefusalsAsync(state, sdk, mcp, admin, official, token);
        await ClaimsAsync(state, sdk, mcp, admin, official, token);
        await SubscriptionIndependentCheckpointR52Repair.ReplayAsync(state, sdk, mcp, admin, official, token);
    }

    private static async Task ProducerAsync(SubscriptionIndependentCheckpointR52State state,
        KeyLoadClient sdk, McpOfficialClient mcp, KeyLoadClient admin, McpOfficialClient official, CancellationToken token)
    {
        if (state.PolicyEpoch == 1)
        {
            await SubscriptionFilterClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
                await sdk.CommitAsync(state.Seed.Publish, token)), state.Seed.PublishReceipt);
        }
        else
        {
            await ChangedAsync(await sdk.CommitAsync(state.Seed.Publish, token));
        }
        await SubscriptionIndependentCheckpointR52Current.RunAsync(state, sdk, mcp, admin, official, token);
        var reply = await mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Seed.Publish, token);
        if (state.PolicyEpoch == 1)
        {
            await SubscriptionFilterClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(reply)).Value, state.Seed.PublishReceipt);
        }
        else
        {
            await SubscriptionIndependentCheckpointR52Refusals.McpAsync(reply, ErrorCode.PermissionDenied);
        }
        await SubscriptionIndependentCheckpointR52Current.RunAsync(state, sdk, mcp, admin, official, token);
        if (state.HealthyPublish is { } publish)
        {
            await SubscriptionFilterClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
                await sdk.CommitAsync(publish, token)), state.HealthyReceipt);
            await SubscriptionIndependentCheckpointR52Current.RunAsync(state, sdk, mcp, admin, official, token);
            await SubscriptionFilterClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await mcp.CallAsync(McpCallerTools.DocumentsCommit, publish, token))).Value, state.HealthyReceipt);
            await SubscriptionIndependentCheckpointR52Current.RunAsync(state, sdk, mcp, admin, official, token);
        }
    }

    private static async Task ReceiptsAsync(SubscriptionIndependentCheckpointR52State state,
        KeyLoadClient sdk, McpOfficialClient mcp, KeyLoadClient admin, McpOfficialClient official, CancellationToken token)
    {
        foreach (var saved in state.Receipts)
        {
            var result = await sdk.CompleteSubscriptionAsync(saved.Request, token);
            if (saved.Epoch == state.PolicyEpoch)
            {
                await SubscriptionFilterClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(result), saved.Receipt);
            }
            else
            {
                await ChangedAsync(result);
            }
            await SubscriptionIndependentCheckpointR52Current.RunAsync(state, sdk, mcp, admin, official, token);
            var reply = await mcp.CallAsync(McpCallerTools.SubscriptionsComplete, saved.Request, token);
            if (saved.Epoch == state.PolicyEpoch)
            {
                await SubscriptionFilterClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(reply)).Value, saved.Receipt);
            }
            else
            {
                await SubscriptionIndependentCheckpointR52Refusals.McpAsync(reply, ErrorCode.PermissionDenied);
            }
            await SubscriptionIndependentCheckpointR52Current.RunAsync(state, sdk, mcp, admin, official, token);
        }
    }

    private static async Task RefusalsAsync(SubscriptionIndependentCheckpointR52State state,
        KeyLoadClient sdk, McpOfficialClient mcp, KeyLoadClient admin, McpOfficialClient official, CancellationToken token)
    {
        foreach (var saved in state.Refusals)
        {
            var result = await sdk.CompleteSubscriptionAsync(saved.Request, token);
            if (saved.Epoch != state.PolicyEpoch)
            {
                await ChangedAsync(result);
            }
            else
            {
                await Assert.That(result.IsFailed).IsTrue();
                await Assert.That(result.Value).IsNull();
                await SubscriptionFilterClusterAssertions.EqualAsync(result.Problem, saved.Failure.Problem);
            }
            await SubscriptionIndependentCheckpointR52Current.RunAsync(state, sdk, mcp, admin, official, token);
            await SubscriptionIndependentCheckpointR52Refusals.McpAsync(
                await mcp.CallAsync(McpCallerTools.SubscriptionsComplete, saved.Request, token),
                saved.Epoch == state.PolicyEpoch ? saved.Code : ErrorCode.PermissionDenied);
            await SubscriptionIndependentCheckpointR52Current.RunAsync(state, sdk, mcp, admin, official, token);
        }
    }

    private static async Task ClaimsAsync(SubscriptionIndependentCheckpointR52State state,
        KeyLoadClient sdk, McpOfficialClient mcp, KeyLoadClient admin, McpOfficialClient official, CancellationToken token)
    {
        foreach (var saved in state.Claims)
        {
            var changed = saved.Epoch != state.PolicyEpoch;
            var settled = state.Receipts.Any(x => x.Request.Subscription == saved.Request.Subscription
                && saved.Result.Deliveries.Any(d => d.Token == x.Request.Token));
            if (!changed && !settled)
            {
                continue;
            }
            var result = await sdk.ReceiveSubscriptionAsync(saved.Request, token);
            if (changed)
            {
                await ChangedAsync(result);
            }
            else
            {
                await SubscriptionIndependentCheckpointR52Refusals.FailureAsync(result,
                ErrorCode.StaleLease, SubscriptionIndependentCheckpointR52Protocol.StaleDetail);
            }
            await SubscriptionIndependentCheckpointR52Current.RunAsync(state, sdk, mcp, admin, official, token);
            await SubscriptionIndependentCheckpointR52Refusals.McpAsync(
                await mcp.CallAsync(McpCallerTools.SubscriptionsReceive, saved.Request, token),
                changed ? ErrorCode.PermissionDenied : ErrorCode.StaleLease);
            await SubscriptionIndependentCheckpointR52Current.RunAsync(state, sdk, mcp, admin, official, token);
        }
    }

    private static Task ChangedAsync<T>(ManagedCode.Communication.Result<T> result)
        => SubscriptionIndependentCheckpointR52Refusals.FailureAsync(result, ErrorCode.PermissionDenied,
            SubscriptionIndependentCheckpointR52Protocol.ChangedOutcomeDetail);
}
