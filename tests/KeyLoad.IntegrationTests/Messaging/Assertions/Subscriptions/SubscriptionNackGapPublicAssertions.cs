using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using TUnit.Assertions.Enums;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class SubscriptionNackGapPublicAssertions
{
    internal static async Task<CommitReceipt> CompleteAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SubscriptionDeliveryCommand request, long position, long version, CancellationToken token)
    {
        var result = await McpCallerAssertions.SdkSuccessAsync(await sdk.CompleteSubscriptionAsync(request, token));
        await SubscriptionFilterClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await mcp.CallAsync(McpCallerTools.SubscriptionsComplete, request, token))).Value, result);
        await Assert.That(result.CommandId).IsEqualTo(request.CommandId);
        await Assert.That(result.Mutations).IsEquivalentTo(new MutationReceipt[] {
            new(request.Action.ToString(), request.Subscription.Source.Resource,
                position.ToString(global::System.Globalization.CultureInfo.InvariantCulture), version) }, CollectionOrdering.Matching);
        return result;
    }

    internal static async Task OriginalAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SubscriptionNackGapPublicState original, CancellationToken token)
    {
        foreach (var saved in original.History)
        {
            await SubscriptionFilterClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
                await sdk.CompleteSubscriptionAsync(saved.Request, token)), saved.Receipt);
            await SubscriptionFilterClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await mcp.CallAsync(McpCallerTools.SubscriptionsComplete, saved.Request, token))).Value, saved.Receipt);
        }
        await SubscriptionPhasePublicAssertions.ReceiveFailureAsync(sdk, mcp, original.Request, ErrorCode.StaleLease, token);
        await SubscriptionFilterClusterAssertions.HistoryAsync(sdk, mcp, original.Request.Subscription.Source, original.Source, token);
    }

    internal static async Task StaleAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SubscriptionNackGapPublicState original, CancellationToken token)
    {
        var group = original.Request.Subscription;
        var before = await McpCallerAssertions.SdkSuccessAsync(await sdk.SubscriptionStatusAsync(group, token));
        foreach (var action in new[] { DeliveryAction.Ack, DeliveryAction.Renew, DeliveryAction.Nack })
        {
            var request = new SubscriptionDeliveryCommand(Guid.NewGuid(), group, original.Claimed.Deliveries[0].Token, action);
            var failure = await sdk.CompleteSubscriptionAsync(request, token);
            await Assert.That(failure.IsFailed).IsTrue();
            await Assert.That(failure.Problem!.ErrorCode).IsEqualTo(ErrorCode.StaleLease.ToString());
            await Assert.That(failure.Problem.Detail).IsEqualTo("The subscription delivery lease is stale.");
            await Assert.That(failure.Value).IsNull();
            var repeated = await sdk.CompleteSubscriptionAsync(request, token);
            await SubscriptionFilterClusterAssertions.EqualAsync(repeated.Problem, failure.Problem);
            await Assert.That(repeated.Value).IsNull();
            var reply = await mcp.CallAsync(McpCallerTools.SubscriptionsComplete, request, token);
            await McpCallerAssertions.ErrorAsync(reply, ErrorCode.StaleLease, true);
            var envelope = reply.StructuredContent ?? throw new InvalidOperationException("The stale lease envelope is absent.");
            await Assert.That(envelope.GetProperty(McpCallerProtocol.Error).GetProperty(McpCallerProtocol.ProblemDetail).GetString())
                .IsEqualTo("The database operation could not be completed.");
            await SubscriptionFilterClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
                await sdk.SubscriptionStatusAsync(group, token)), before);
        }
        await OriginalAsync(sdk, mcp, original, token);
    }
}
