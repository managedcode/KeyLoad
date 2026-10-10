using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class EventMessageSensitivePublicAssertions
{
    internal static async Task FullAsync(object received, EventMessageSensitivePublicState state)
    {
        string payload;
        string headers;
        if (received is ReceiveSubscriptionResult subscription)
        {
            var delivery = await Assert.That(subscription.Deliveries).HasSingleItem();
            await Assert.That(delivery.Event.Source).IsEqualTo(state.Source);
            await Assert.That(delivery.Event.Data.EventId).IsEqualTo(EventMessageSensitivePublicProtocol.Message);
            await Assert.That(delivery.Event.Data.EventType).IsEqualTo(EventMessageSensitivePublicProtocol.EventType);
            payload = delivery.Event.Data.PayloadJson;
            headers = delivery.Event.Data.HeadersJson;
        }
        else
        {
            var delivery = await Assert.That(((ReceiveResult)received).Deliveries).HasSingleItem();
            await Assert.That(delivery.Id).IsEqualTo(EventMessageSensitivePublicProtocol.Message);
            payload = delivery.PayloadJson;
            headers = delivery.HeadersJson;
        }
        await Assert.That(payload).IsEqualTo(EventMessageSensitivePublicProtocol.Payload);
        await Assert.That(headers).IsEqualTo(EventMessageSensitivePublicProtocol.Headers);
    }

    internal static async Task ReplayAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers caller,
        EventMessageSensitivePublicState state, CancellationToken token)
    {
        foreach (var route in QueueLifecyclePublicProtocol.Routes)
        {
            var actual = await EventMessageSensitivePublicOperations.ReceiveAsync(caller, state, state.HealthyRequest, route, token);
            await SameAsync(state.Healthy, actual);
            await FullAsync(actual, state);
        }
        await ReceiptReplayAsync(root, caller, state, token);
    }

    internal static async Task ReceiptReplayAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers caller,
        EventMessageSensitivePublicState state, CancellationToken token)
    {
        foreach (var route in QueueLifecyclePublicProtocol.Routes)
        {
            foreach (var (command, receipt) in state.Commands)
            {
                await SameAsync(receipt, await QueueLifecyclePublicRoutes.CallAsync(root, route, state.Partition,
                    McpCallerTools.DocumentsCommit, command, () => root.Sdk.CommitAsync(command, token), token));
            }
            foreach (var (command, receipt) in state.Completions)
            {
                if (command is SubscriptionDeliveryCommand subscription)
                {
                    await SameAsync(receipt, await QueueLifecyclePublicRoutes.CallAsync(caller, route, state.Partition,
                        McpCallerTools.SubscriptionsComplete, subscription, () => caller.Sdk.CompleteSubscriptionAsync(subscription, token), token));
                }
                else
                {
                    var queue = (DeliveryCommand)command;
                    await SameAsync(receipt, await QueueLifecyclePublicRoutes.CallAsync(caller, route, state.Partition,
                        McpCallerTools.MessagesComplete, queue, () => caller.Sdk.CompleteAsync(queue, token), token));
                }
            }
        }
    }

    private static async Task SameAsync(object expected, object actual)
        => await Assert.That(NativeSerialization.Serialize(actual).SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
}
