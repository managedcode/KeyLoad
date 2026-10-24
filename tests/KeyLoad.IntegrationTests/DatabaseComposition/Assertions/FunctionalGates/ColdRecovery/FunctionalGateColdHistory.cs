using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.DatabaseComposition;

internal static class FunctionalGateColdHistory
{
    internal static async Task VerifyAsync(FunctionalGateContext context, FunctionalGateColdModel model, CancellationToken token)
    {
        var stream = new ReadStreamRequest(model.Event.Stream, Limit: 2);
        var direct = await context.SdkAsync(context.Reader.Sdk.ReadStreamAsync(stream, token));
        var official = await context.McpAsync<StreamPage>(context.Reader, McpCallerTools.StreamsRead, stream, token);
        foreach (var page in new[] { direct, official })
        {
            await SqlClusterProtocol.EqualAsync(model.Event.Stream, page.Stream);
            await SqlClusterProtocol.EqualAsync(new StreamHead(1, 1, 1), page.Head);
            await SqlClusterProtocol.EqualAsync(new[] { model.Event }, page.Events.ToArray());
            await Assert.That(page.CutPosition).IsGreaterThanOrEqualTo(model.Receipt.Token.Position);
            await Assert.That(page.HasMore).IsFalse();
            await Assert.That(page.Cursor).IsNull();
        }
        var topic = new ReadEventSourceRequest(model.TopicEvent.Source, Limit: 2);
        var sourceDirect = await context.SdkAsync(context.Reader.Sdk.ReadEventSourceAsync(topic, token));
        var sourceOfficial = await context.McpAsync<EventSourcePage>(context.Reader, McpCallerTools.EventsRead, topic, token);
        foreach (var page in new[] { sourceDirect, sourceOfficial })
        {
            await SqlClusterProtocol.EqualAsync(model.TopicEvent.Source, page.Source);
            await SqlClusterProtocol.EqualAsync(new EventSourceHead(1, 1, 1), page.Head);
            await SqlClusterProtocol.EqualAsync(new[] { model.TopicEvent }, page.Events.ToArray());
            await Assert.That(page.CutPosition).IsGreaterThanOrEqualTo(model.Receipt.Token.Position);
            await Assert.That(page.HasMore).IsFalse();
        }
        var queue = new InspectMessageRequest(new(context.Partition, FunctionalGateColdProtocol.Resource("queue", model.Index)), FunctionalGateColdProtocol.Id);
        await SqlClusterProtocol.EqualAsync(model.Message, await context.SdkAsync(context.Reader.Sdk.InspectAsync(queue, token)));
        await SqlClusterProtocol.EqualAsync(model.Message, await context.McpAsync<MessageInspectionResult>(context.Reader, McpCallerTools.MessagesInspect, queue, token));
        await SqlClusterProtocol.EqualAsync(model.Message, await context.SdkAsync(context.QueueAdministrator.Sdk.InspectAsync(queue, token)));
        await SqlClusterProtocol.EqualAsync(model.Message, await context.McpAsync<MessageInspectionResult>(context.QueueAdministrator, McpCallerTools.MessagesInspect, queue, token));
    }
}
