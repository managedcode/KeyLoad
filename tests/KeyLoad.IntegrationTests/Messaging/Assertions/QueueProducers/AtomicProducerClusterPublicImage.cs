using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class AtomicProducerClusterPublicImage
{
    internal static async Task SdkAsync(KeyLoadClient sdk, QueueProducerClusterSeed seed, string id,
        QueueProducerClusterImage image, CancellationToken token)
    {
        await QueueProducerClusterAssertions.EqualAsync(await SqlClusterProtocol.SdkAsync<DocumentResult?>(sdk,
            SqlClusterProtocol.Call(seed.Lane.Partition, McpCallerTools.DocumentsGet,
                new GetDocumentRequest(QueueProducerClusterAssertions.Document(seed, id))), token), image.Document);
        await QueueProducerClusterAssertions.EqualAsync(await SqlClusterProtocol.SdkAsync<MessageInspection?>(sdk,
            SqlClusterProtocol.Call(seed.Lane.Partition, McpCallerTools.MessagesInspect, new InspectMessageRequest(seed.Lane, id)), token), image.Ready);
        await QueueProducerClusterAssertions.EqualAsync(await SqlClusterProtocol.SdkAsync<MessageInspection?>(sdk,
            SqlClusterProtocol.Call(seed.Lane.Partition, McpCallerTools.MessagesInspect,
                new InspectMessageRequest(seed.Lane, QueueProducerClusterProtocol.Scheduled)), token), image.Scheduled);
    }

    internal static async Task McpAsync(McpOfficialClient mcp, QueueProducerClusterSeed seed, string id,
        QueueProducerClusterImage image, CancellationToken token)
    {
        await QueueProducerClusterAssertions.EqualAsync(await SqlClusterProtocol.McpAsync<DocumentResult?>(mcp,
            SqlClusterProtocol.Call(seed.Lane.Partition, McpCallerTools.DocumentsGet,
                new GetDocumentRequest(QueueProducerClusterAssertions.Document(seed, id))), token), image.Document);
        await QueueProducerClusterAssertions.EqualAsync(await SqlClusterProtocol.McpAsync<MessageInspection?>(mcp,
            SqlClusterProtocol.Call(seed.Lane.Partition, McpCallerTools.MessagesInspect, new InspectMessageRequest(seed.Lane, id)), token), image.Ready);
        await QueueProducerClusterAssertions.EqualAsync(await SqlClusterProtocol.McpAsync<MessageInspection?>(mcp,
            SqlClusterProtocol.Call(seed.Lane.Partition, McpCallerTools.MessagesInspect,
                new InspectMessageRequest(seed.Lane, QueueProducerClusterProtocol.Scheduled)), token), image.Scheduled);
    }
}
