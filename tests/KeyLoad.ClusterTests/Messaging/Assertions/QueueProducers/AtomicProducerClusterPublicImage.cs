using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.Messaging;

internal static class AtomicProducerClusterPublicImage
{
    internal static async Task SdkAsync(KeyLoadClient sdk, QueueProducerClusterSeed seed, string id,
        QueueProducerClusterImage image, CancellationToken token)
    {
        await QueueProducerClusterAssertions.EqualAsync((await SqlClusterProtocol.SdkAsync<DocumentReadResult>(sdk,
            SqlClusterProtocol.Call(seed.Lane.Partition, McpCallerTools.DocumentsGet,
                new GetDocumentRequest(QueueProducerClusterAssertions.Document(seed, id))), token)).Document, image.Document);
        await QueueProducerClusterAssertions.EqualAsync((await SqlClusterProtocol.SdkAsync<MessageInspectionResult>(sdk,
            SqlClusterProtocol.Call(seed.Lane.Partition, McpCallerTools.MessagesInspect, new InspectMessageRequest(seed.Lane, id)), token)).Message, image.Ready);
        await QueueProducerClusterAssertions.EqualAsync((await SqlClusterProtocol.SdkAsync<MessageInspectionResult>(sdk,
            SqlClusterProtocol.Call(seed.Lane.Partition, McpCallerTools.MessagesInspect,
                new InspectMessageRequest(seed.Lane, QueueProducerClusterProtocol.Scheduled)), token)).Message, image.Scheduled);
    }

    internal static async Task McpAsync(McpOfficialClient mcp, QueueProducerClusterSeed seed, string id,
        QueueProducerClusterImage image, CancellationToken token)
    {
        await QueueProducerClusterAssertions.EqualAsync((await SqlClusterProtocol.McpAsync<DocumentReadResult>(mcp,
            SqlClusterProtocol.Call(seed.Lane.Partition, McpCallerTools.DocumentsGet,
                new GetDocumentRequest(QueueProducerClusterAssertions.Document(seed, id))), token)).Document, image.Document);
        await QueueProducerClusterAssertions.EqualAsync((await SqlClusterProtocol.McpAsync<MessageInspectionResult>(mcp,
            SqlClusterProtocol.Call(seed.Lane.Partition, McpCallerTools.MessagesInspect, new InspectMessageRequest(seed.Lane, id)), token)).Message, image.Ready);
        await QueueProducerClusterAssertions.EqualAsync((await SqlClusterProtocol.McpAsync<MessageInspectionResult>(mcp,
            SqlClusterProtocol.Call(seed.Lane.Partition, McpCallerTools.MessagesInspect,
                new InspectMessageRequest(seed.Lane, QueueProducerClusterProtocol.Scheduled)), token)).Message, image.Scheduled);
    }
}
