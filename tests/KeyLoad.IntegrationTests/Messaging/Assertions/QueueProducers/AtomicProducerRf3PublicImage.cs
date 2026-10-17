using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class AtomicProducerRf3PublicImage
{
    internal static async Task SdkAsync(KeyLoadClient sdk, QueueProducerRf3Seed seed, string id,
        QueueProducerRf3Image image, CancellationToken token)
    {
        await QueueProducerRf3Assertions.EqualAsync(await SqlRf3Protocol.SdkAsync<DocumentResult?>(sdk,
            SqlRf3Protocol.Call(seed.Lane.Partition, McpCallerTools.DocumentsGet,
                new GetDocumentRequest(QueueProducerRf3Assertions.Document(seed, id))), token), image.Document);
        await QueueProducerRf3Assertions.EqualAsync(await SqlRf3Protocol.SdkAsync<MessageInspection?>(sdk,
            SqlRf3Protocol.Call(seed.Lane.Partition, McpCallerTools.MessagesInspect, new InspectMessageRequest(seed.Lane, id)), token), image.Ready);
        await QueueProducerRf3Assertions.EqualAsync(await SqlRf3Protocol.SdkAsync<MessageInspection?>(sdk,
            SqlRf3Protocol.Call(seed.Lane.Partition, McpCallerTools.MessagesInspect,
                new InspectMessageRequest(seed.Lane, QueueProducerRf3Protocol.Scheduled)), token), image.Scheduled);
    }

    internal static async Task McpAsync(McpOfficialClient mcp, QueueProducerRf3Seed seed, string id,
        QueueProducerRf3Image image, CancellationToken token)
    {
        await QueueProducerRf3Assertions.EqualAsync(await SqlRf3Protocol.McpAsync<DocumentResult?>(mcp,
            SqlRf3Protocol.Call(seed.Lane.Partition, McpCallerTools.DocumentsGet,
                new GetDocumentRequest(QueueProducerRf3Assertions.Document(seed, id))), token), image.Document);
        await QueueProducerRf3Assertions.EqualAsync(await SqlRf3Protocol.McpAsync<MessageInspection?>(mcp,
            SqlRf3Protocol.Call(seed.Lane.Partition, McpCallerTools.MessagesInspect, new InspectMessageRequest(seed.Lane, id)), token), image.Ready);
        await QueueProducerRf3Assertions.EqualAsync(await SqlRf3Protocol.McpAsync<MessageInspection?>(mcp,
            SqlRf3Protocol.Call(seed.Lane.Partition, McpCallerTools.MessagesInspect,
                new InspectMessageRequest(seed.Lane, QueueProducerRf3Protocol.Scheduled)), token), image.Scheduled);
    }
}
