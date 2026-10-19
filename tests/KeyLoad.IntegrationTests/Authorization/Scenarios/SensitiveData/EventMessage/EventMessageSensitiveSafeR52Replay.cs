using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.IntegrationTests.Features.QueryExecution;
namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class EventMessageSensitiveSafeR52Replay
{
    internal static async Task ReceiveAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers caller,
        EventMessageSensitiveSafeR52State state, object request, object original, string id, CancellationToken token)
    {
        foreach (var route in QueueLifecyclePublicProtocol.Routes)
        {
            var actual = await EventMessageSensitivePublicOperations.ReceiveAsync(caller, state.Model, request, route, token);
            await SameAsync(original, actual);
            await LiteralAsync(actual, state, id);
            await EventMessageSensitiveSafeR52Current.RequireAsync(root, state, token);
        }
    }

    internal static async Task ReceiptsAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers worker,
        EventMessageSensitiveSafeR52State state, CancellationToken token)
    {
        foreach (var route in QueueLifecyclePublicProtocol.Routes)
        {
            foreach (var (command, receipt) in state.Producers)
            {
                var actual = await QueueLifecyclePublicRoutes.CallAsync(root, route, state.Model.Partition,
                    McpCallerTools.DocumentsCommit, command, () => root.Sdk.CommitAsync(command, token), token);
                await SqlClusterProtocol.EqualAsync(receipt, actual);
                await EventMessageSensitiveSafeR52Current.RequireAsync(root, state, token);
            }
            foreach (var (command, receipt) in state.Completions)
            {
                CommitReceipt actual;
                if (command is SubscriptionDeliveryCommand subscription)
                {
                    actual = await QueueLifecyclePublicRoutes.CallAsync(worker, route, state.Model.Partition,
                    McpCallerTools.SubscriptionsComplete, subscription, () => worker.Sdk.CompleteSubscriptionAsync(subscription, token), token);
                }
                else
                {
                    var queue = (DeliveryCommand)command;
                    actual = await QueueLifecyclePublicRoutes.CallAsync(worker, route, state.Model.Partition,
                        McpCallerTools.MessagesComplete, queue, () => worker.Sdk.CompleteAsync(queue, token), token);
                }
                await SqlClusterProtocol.EqualAsync(receipt, actual);
                await EventMessageSensitiveSafeR52Current.RequireAsync(root, state, token);
            }
        }
    }

    internal static async Task LiteralAsync(object received, EventMessageSensitiveSafeR52State state, string id)
    {
        string payload;
        string headers;
        if (received is ReceiveSubscriptionResult subscription)
        {
            var delivery = await Assert.That(subscription.Deliveries).HasSingleItem();
            await Assert.That(delivery.Event.Source).IsEqualTo(state.Model.Source);
            await Assert.That(delivery.Event.Data.EventId).IsEqualTo(id);
            await Assert.That(delivery.Event.Data.EventType).IsEqualTo(EventMessageSensitiveSafeR52Protocol.EventType);
            payload = delivery.Event.Data.PayloadJson;
            headers = delivery.Event.Data.HeadersJson;
        }
        else
        {
            var delivery = await Assert.That(((ReceiveResult)received).Deliveries).HasSingleItem();
            await Assert.That(delivery.Id).IsEqualTo(id);
            payload = delivery.PayloadJson;
            headers = delivery.HeadersJson;
        }
        await Assert.That(payload).IsEqualTo(EventMessageSensitiveSafeR52Protocol.Payload);
        await Assert.That(headers).IsEqualTo(EventMessageSensitiveSafeR52Protocol.Headers);
    }

    internal static async Task SameAsync(object expected, object actual)
        => await Assert.That(OrleansSerialization.Serialize(actual).SequenceEqual(OrleansSerialization.Serialize(expected))).IsTrue();
}
