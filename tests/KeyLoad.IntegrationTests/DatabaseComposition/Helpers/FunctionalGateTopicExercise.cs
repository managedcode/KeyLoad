using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateTopicExercise
{
    internal static async Task InitializeAsync(FunctionalGateContext ctx, int index, CancellationToken token)
    {
        var subscription = Subscription(ctx, index);
        var data = new EventData("gate_baseline", "gate_link", FunctionalGateQueues.Payload(ctx, -1));
        var command = new CommandRequest(Guid.NewGuid(), ctx.Partition, [new PublishTopic(subscription.Source.Resource, [data])]);
        var receipt = await ctx.SdkAsync(ctx.Administrator.Sdk.CommitAsync(command, token));
        await FunctionalGateQueues.ReceiptAsync(ctx, receipt, command.CommandId,
            new("publishTopic", subscription.Source.Resource, data.EventId, 1));
        await Assert.That(ctx.WriterPrincipalId).IsNotEmpty();
        var definition = new SubscriptionDefinition(ctx.WriterPrincipalId);
        var request = new ConfigureSubscriptionRequest(Guid.NewGuid(), subscription, definition, SubscriptionStart.FromNow);
        var configured = await ctx.SdkAsync(ctx.Administrator.Sdk.ConfigureSubscriptionAsync(request, token));
        await FunctionalGateQueues.EqualAsync(new SubscriptionInfo(subscription, definition, 1, 1, 1, 1, 1, false, null), configured);
        await FunctionalGateQueues.EqualAsync(configured, await ctx.McpAsync<SubscriptionInfo>(ctx.Administrator,
            McpCallerTools.SubscriptionsConfigure, request, token));
    }

    internal static async Task RunAsync(FunctionalGateContext ctx, int iteration, int resourceIndex, CancellationToken token)
    {
        var subscription = Subscription(ctx, resourceIndex);
        var before = await ctx.SdkAsync(ctx.Writer.Sdk.SubscriptionStatusAsync(subscription, token));
        await Assert.That(before.Checkpoint).IsEqualTo(before.TailPosition);
        await Assert.That(before.IssuedPosition).IsEqualTo(before.TailPosition);
        await Assert.That(before.Paused).IsFalse();
        await Assert.That(before.SafeFailureCode).IsNull();
        var data = new EventData("gate_event_" + FunctionalGateQueues.Id(iteration), "gate_link",
            FunctionalGateQueues.Payload(ctx, iteration), CorrelationId: FunctionalGateQueues.Id(iteration));
        var command = new CommandRequest(Guid.NewGuid(), ctx.Partition, [new PublishTopic(subscription.Source.Resource, [data])]);
        await RefuseAsync(ctx, command, subscription, before, token);
        var published = await ctx.McpAsync<CommitReceipt>(ctx.Writer, McpCallerTools.DocumentsCommit, command, token);
        var position = checked(before.TailPosition + 1);
        await FunctionalGateQueues.ReceiptAsync(ctx, published, command.CommandId,
            new("publishTopic", subscription.Source.Resource, data.EventId, position));
        await FunctionalGateQueues.EqualAsync(published, await ctx.SdkAsync(ctx.Writer.Sdk.CommitAsync(command, token)));
        var request = new ReceiveSubscriptionRequest(Guid.NewGuid(), subscription);
        var received = await ctx.SdkAsync(ctx.Writer.Sdk.ReceiveSubscriptionAsync(request, token));
        await Assert.That(received.RequestId).IsEqualTo(request.RequestId);
        var delivery = await Assert.That(received.Deliveries).HasSingleItem();
        await FunctionalGateQueues.EqualAsync(data, delivery.Event.Data);
        await FunctionalGateQueues.EqualAsync(subscription.Source, delivery.Event.Source);
        await Assert.That(delivery.Event.Position).IsEqualTo(position);
        await Assert.That(delivery.Event.EventSequence).IsGreaterThan(0);
        await Assert.That(delivery.Attempt).IsEqualTo(1);
        await Assert.That(delivery.LeaseVersion).IsEqualTo(1);
        await Assert.That(delivery.Token).IsNotEmpty();
        await FunctionalGateQueues.EqualAsync(received,
            await ctx.McpAsync<ReceiveSubscriptionResult>(ctx.Writer, McpCallerTools.SubscriptionsReceive, request, token));
        var ack = new SubscriptionDeliveryCommand(Guid.NewGuid(), subscription, delivery.Token, DeliveryAction.Ack);
        var acknowledged = await ctx.McpAsync<CommitReceipt>(ctx.Writer, McpCallerTools.SubscriptionsComplete, ack, token);
        await FunctionalGateQueues.ReceiptAsync(ctx, acknowledged, ack.CommandId, new("Ack", subscription.Source.Resource,
            position.ToString(System.Globalization.CultureInfo.InvariantCulture), 1));
        await FunctionalGateQueues.EqualAsync(acknowledged, await ctx.SdkAsync(ctx.Writer.Sdk.CompleteSubscriptionAsync(ack, token)));
        var expected = before with
        {
            Checkpoint = position,
            IssuedPosition = position,
            TailPosition = position
        };
        await FunctionalGateQueues.EqualAsync(expected, await ctx.SdkAsync(ctx.Writer.Sdk.SubscriptionStatusAsync(subscription, token)));
        await FunctionalGateQueues.EqualAsync(expected, await ctx.McpAsync<SubscriptionInfo>(ctx.Writer,
            McpCallerTools.SubscriptionsStatus, new GetSubscriptionRequest(subscription), token));
        var read = new ReadEventSourceRequest(subscription.Source, position - 1, 1);
        var page = await ctx.SdkAsync(ctx.Writer.Sdk.ReadEventSourceAsync(read, token));
        await FunctionalGateQueues.EqualAsync(delivery.Event, await Assert.That(page.Events).HasSingleItem());
        var official = await ctx.McpAsync<EventSourcePage>(ctx.Writer, McpCallerTools.EventsRead, read, token);
        await FunctionalGateQueues.EqualAsync(delivery.Event, await Assert.That(official.Events).HasSingleItem());
        await FunctionalGateQueues.EqualAsync(subscription.Source, page.Source);
        await FunctionalGateQueues.EqualAsync(subscription.Source, official.Source);
        await FunctionalGateQueues.EqualAsync(new EventSourceHead(position, 1, 1), page.Head);
        await FunctionalGateQueues.EqualAsync(page.Head, official.Head);
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(official.HasMore).IsFalse();
        await Assert.That(page.CutPosition).IsGreaterThanOrEqualTo(published.Token.Position);
        await Assert.That(official.CutPosition).IsGreaterThanOrEqualTo(published.Token.Position);
        await FunctionalGateQueues.EqualAsync(published, await ctx.SdkAsync(ctx.Writer.Sdk.CommitAsync(command, token)));
    }

    internal static async Task VerifyBaselineAsync(FunctionalGateContext ctx, int index, CancellationToken token)
    {
        var source = Subscription(ctx, index).Source;
        var request = new ReadEventSourceRequest(source, 0, 1);
        var sdk = await ctx.SdkAsync(ctx.Administrator.Sdk.ReadEventSourceAsync(request, token));
        var official = await ctx.McpAsync<EventSourcePage>(ctx.Administrator, McpCallerTools.EventsRead, request, token);
        var actual = await Assert.That(sdk.Events).HasSingleItem();
        await FunctionalGateQueues.EqualAsync(source, actual.Source);
        await Assert.That(actual.Position).IsEqualTo(1);
        await FunctionalGateQueues.EqualAsync(new EventData("gate_baseline", "gate_link", FunctionalGateQueues.Payload(ctx, -1)), actual.Data);
        await FunctionalGateQueues.EqualAsync(actual, await Assert.That(official.Events).HasSingleItem());
    }

    private static SubscriptionRef Subscription(FunctionalGateContext ctx, int index)
        => new(new(ctx.Partition, FunctionalGateQueues.Topic(index), EventSourceKind.Topic), "gate_group");

    private static async Task RefuseAsync(FunctionalGateContext ctx, CommandRequest command, SubscriptionRef subscription,
        SubscriptionInfo before, CancellationToken token)
    {
        var denied = await ctx.Reader.Sdk.CommitAsync(command with
        {
            CommandId = Guid.NewGuid()
        }, token);
        await Assert.That(denied.IsSuccess).IsFalse();
        await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(ErrorCode.PermissionDenied.ToString());
        await McpCallerAssertions.ErrorAsync(await ctx.Reader.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command with
        {
            CommandId = Guid.NewGuid()
        }, token),
            ErrorCode.PermissionDenied, dispatched: true);
        await FunctionalGateQueues.EqualAsync(before, await ctx.SdkAsync(ctx.Writer.Sdk.SubscriptionStatusAsync(subscription, token)));
    }
}
