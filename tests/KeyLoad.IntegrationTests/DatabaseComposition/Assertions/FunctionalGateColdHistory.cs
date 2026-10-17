using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateColdHistory
{
    internal static async Task VerifyAsync(FunctionalGateContext context, FunctionalGateColdModel model, CancellationToken token)
    {
        var stream = new ReadStreamRequest(model.Event.Stream, Limit: 2);
        var direct = await context.SdkAsync(context.Reader.Sdk.ReadStreamAsync(stream, token));
        var official = await context.McpAsync<StreamPage>(context.Reader, McpCallerTools.StreamsRead, stream, token);
        foreach (var page in new[] { direct, official })
        {
            await SqlRf3Protocol.EqualAsync(model.Event.Stream, page.Stream);
            await SqlRf3Protocol.EqualAsync(new StreamHead(1, 1, 1), page.Head);
            await SqlRf3Protocol.EqualAsync(new[] { model.Event }, page.Events.ToArray());
            await Assert.That(page.CutPosition).IsGreaterThanOrEqualTo(model.Receipt.Token.Position);
            await Assert.That(page.HasMore).IsFalse();
            await Assert.That(page.Cursor).IsNull();
        }
        var topic = new ReadEventSourceRequest(model.TopicEvent.Source, Limit: 2);
        var sourceDirect = await context.SdkAsync(context.Reader.Sdk.ReadEventSourceAsync(topic, token));
        var sourceOfficial = await context.McpAsync<EventSourcePage>(context.Reader, McpCallerTools.EventsRead, topic, token);
        foreach (var page in new[] { sourceDirect, sourceOfficial })
        {
            await SqlRf3Protocol.EqualAsync(model.TopicEvent.Source, page.Source);
            await SqlRf3Protocol.EqualAsync(new EventSourceHead(1, 1, 1), page.Head);
            await SqlRf3Protocol.EqualAsync(new[] { model.TopicEvent }, page.Events.ToArray());
            await Assert.That(page.CutPosition).IsGreaterThanOrEqualTo(model.Receipt.Token.Position);
            await Assert.That(page.HasMore).IsFalse();
        }
        var queue = new InspectMessageRequest(new(context.Partition, FunctionalGateColdProtocol.Resource("queue", model.Index)), FunctionalGateColdProtocol.Id);
        await SqlRf3Protocol.EqualAsync(model.Message, await context.SdkAsync(context.Reader.Sdk.InspectAsync(queue, token)));
        await SqlRf3Protocol.EqualAsync(model.Message, await context.McpAsync<MessageInspectionResult>(context.Reader, McpCallerTools.MessagesInspect, queue, token));
        await SqlRf3Protocol.EqualAsync(model.Message, await context.SdkAsync(context.QueueAdministrator.Sdk.InspectAsync(queue, token)));
        await SqlRf3Protocol.EqualAsync(model.Message, await context.McpAsync<MessageInspectionResult>(context.QueueAdministrator, McpCallerTools.MessagesInspect, queue, token));
    }
}
