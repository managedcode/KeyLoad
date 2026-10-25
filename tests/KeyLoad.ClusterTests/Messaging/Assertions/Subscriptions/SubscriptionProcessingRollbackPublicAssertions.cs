using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.Messaging;

internal static class SubscriptionProcessingRollbackPublicAssertions
{
    private const string First = "inbox-first";
    private const string Second = "inbox-second";
    private const long MissingRevision = 9;

    internal static async Task<SubscriptionProcessingRequest> StartAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SubscriptionProcessingRequest original, ReceiveSubscriptionRequest claim,
        ReceiveSubscriptionResult claimed, EventSourcePage source, CancellationToken token)
    {
        var failed = original with
        {
            CommandId = Guid.NewGuid(),
            Effects = [SubscriptionInboxPublicAssertions.Effects[0],
                new PutDocument(SubscriptionFilterClusterProtocol.Collection, Second, "{\"n\":2}", MissingRevision)]
        };
        for (var repeat = 0; repeat < 2; repeat++)
        {
            await RefusalAsync(sdk, mcp, failed, token);
            foreach (var id in new[] { First, Second })
            {
                await Assert.That(await PublicReadAssertions.DocumentAsync(await sdk.GetAsync(
                    new(failed.Subscription.Source.Partition, SubscriptionFilterClusterProtocol.Collection, id), token))).IsNull();
            }
            await SubscriptionFilterClusterAssertions.StatusAsync(sdk, mcp, failed.Subscription, 1L, 0L, false, token);
            await SubscriptionFilterClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
                await sdk.ReceiveSubscriptionAsync(claim, token)), claimed);
            await SubscriptionFilterClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<ReceiveSubscriptionResult>(
                await mcp.CallAsync(McpCallerTools.SubscriptionsReceive, claim, token))).Value, claimed);
            await SubscriptionFilterClusterAssertions.HistoryAsync(sdk, mcp, failed.Subscription.Source, source, token);
        }
        return failed;
    }

    internal static async Task ReplayAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SubscriptionInboxPublicState state, CancellationToken token)
    {
        var failed = state.FailedProcessing
            ?? throw new InvalidOperationException("The original failed subscription processing request is absent.");
        await RefusalAsync(sdk, mcp, failed, token);
        await SubscriptionInboxPublicAssertions.DocumentsAsync(sdk, failed.Subscription, token);
        await SubscriptionFilterClusterAssertions.HistoryAsync(sdk, mcp, failed.Subscription.Source, state.Source, token);
    }

    private static async Task RefusalAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SubscriptionProcessingRequest request, CancellationToken token)
    {
        var before = await McpCallerAssertions.SdkSuccessAsync(await sdk.SubscriptionStatusAsync(request.Subscription, token));
        var result = await sdk.CommitSubscriptionProcessingAsync(request, token);
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        await Assert.That(result.Problem!.ErrorCode).IsEqualTo(ErrorCode.RevisionConflict.ToString());
        _ = (await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.SubscriptionsProcess, request, token),
            ErrorCode.RevisionConflict, true)).Value;
        await SubscriptionFilterClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await sdk.SubscriptionStatusAsync(request.Subscription, token)), before);
        await SubscriptionFilterClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<SubscriptionInfo>(
            await mcp.CallAsync(McpCallerTools.SubscriptionsStatus, new GetSubscriptionRequest(request.Subscription), token))).Value, before);
    }
}
