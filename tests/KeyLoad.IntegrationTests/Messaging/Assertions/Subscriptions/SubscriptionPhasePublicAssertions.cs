using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using TUnit.Assertions.Enums;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class SubscriptionPhasePublicAssertions
{
    internal static ReceiveSubscriptionRequest Receive(SubscriptionRef group)
        => new(Guid.NewGuid(), group, MaxEvents: SubscriptionFilterClusterProtocol.Window,
            LeaseSeconds: new SubscriptionPolicy().MaxLeaseSeconds);

    internal static async Task<ReceiveSubscriptionResult> ClaimAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        ReceiveSubscriptionRequest request, EventSourcePage source, long[] positions, CancellationToken token)
    {
        var result = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveSubscriptionAsync(request, token));
        var replay = (await McpCallerAssertions.SuccessAsync<ReceiveSubscriptionResult>(
            await mcp.CallAsync(McpCallerTools.SubscriptionsReceive, request, token))).Value;
        await SubscriptionFilterClusterAssertions.EqualAsync(replay, result);
        await Assert.That(result.Deliveries.Select(d => d.Event.Position)).IsEquivalentTo(positions, CollectionOrdering.Matching);
        foreach (var delivery in result.Deliveries)
        {
            await SubscriptionFilterClusterAssertions.EqualAsync(delivery.Event, source.Events.Single(e => e.Position == delivery.Event.Position));
            await Assert.That(delivery.LeaseVersion).IsEqualTo(1L);
            await Assert.That(delivery.Attempt).IsEqualTo(1);
            await Assert.That(delivery.Token).IsNotEmpty();
        }
        return result;
    }

    internal static async Task<CommitReceipt> CompleteAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SubscriptionDeliveryCommand request, long position, CancellationToken token)
    {
        var result = await McpCallerAssertions.SdkSuccessAsync(await sdk.CompleteSubscriptionAsync(request, token));
        await SubscriptionFilterClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await mcp.CallAsync(McpCallerTools.SubscriptionsComplete, request, token))).Value, result);
        await ReceiptAsync(result, request, position);
        return result;
    }

    internal static async Task ReceiptAsync(CommitReceipt receipt, SubscriptionDeliveryCommand request, long position)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(request.CommandId);
        await Assert.That(receipt.Mutations).IsEquivalentTo(new MutationReceipt[] {
            new(request.Action.ToString(), request.Subscription.Source.Resource,
                position.ToString(global::System.Globalization.CultureInfo.InvariantCulture), 1L) }, CollectionOrdering.Matching);
    }

    internal static async Task<string> ReceiveFailureAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        ReceiveSubscriptionRequest request, ErrorCode code, CancellationToken token)
    {
        var failure = await sdk.ReceiveSubscriptionAsync(request, token);
        await Assert.That(failure.IsFailed).IsTrue();
        await Assert.That(failure.Problem!.ErrorCode).IsEqualTo(code.ToString());
        await Assert.That(failure.Value).IsNull();
        var reply = await mcp.CallAsync(McpCallerTools.SubscriptionsReceive, request, token);
        _ = (await McpCallerAssertions.ErrorAsync(reply, code, true)).Value;
        if (code == ErrorCode.DispatchPaused)
        {
            var envelope = reply.StructuredContent ?? throw new InvalidOperationException("The canonical pause envelope is absent.");
            var problem = envelope.GetProperty(McpCallerProtocol.Error);
            await Assert.That(problem.GetProperty(McpCallerProtocol.ProblemType).GetString())
                .IsEqualTo("urn:keyload:error:DispatchPaused");
            await Assert.That(problem.GetProperty(McpCallerProtocol.ProblemTitle).GetString()).IsEqualTo("DispatchPaused");
            await Assert.That(problem.GetProperty(McpCallerProtocol.ProblemStatus).GetInt32()).IsEqualTo(400);
            await Assert.That(problem.GetProperty(McpCallerProtocol.ProblemDetail).GetString())
                .IsEqualTo("The database operation could not be completed.");
        }
        return global::System.Text.Json.JsonSerializer.Serialize(failure.Problem, JsonDefaults.Options);
    }

    internal static async Task HealthyAsync(KeyLoadClient sdk, McpOfficialClient mcp, SubscriptionRef group,
        EventSourcePage source, long position, CancellationToken token)
    {
        var request = new ReceiveSubscriptionRequest(Guid.NewGuid(), group, LeaseSeconds: new SubscriptionPolicy().MaxLeaseSeconds);
        var claimed = await ClaimAsync(sdk, mcp, request, source, [position], token);
        var ack = new SubscriptionDeliveryCommand(Guid.NewGuid(), group, claimed.Deliveries.Single().Token, DeliveryAction.Ack);
        await CompleteAsync(sdk, mcp, ack, position, token);
        await ReceiveFailureAsync(sdk, mcp, request, ErrorCode.StaleLease, token);
    }
}
