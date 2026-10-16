using KeyLoad.IntegrationTests.Features.DatabaseComposition;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class ResourcePartitionInventoryModelsHistory
{
    private const int OneEventPlusTerminalBound = 2;

    internal static async Task<FunctionalGateColdModel> CaptureAsync(FunctionalGateContext context,
        ResourcePartitionInventoryModelsBatch original, ResourcePartitionInventoryModelsTableOriginal table,
        FunctionalGateColdBlob blob, DateTimeOffset beforeWrite, CancellationToken token)
    {
        var stream = await context.SdkAsync(context.Reader.Sdk.ReadStreamAsync(new(new(context.Partition,
            ResourcePartitionInventoryModelsProtocol.Resource("events"), FunctionalGateColdProtocol.Id),
            Limit: OneEventPlusTerminalBound), token));
        var source = new EventSourceRef(context.Partition,
            ResourcePartitionInventoryModelsProtocol.Resource("topic"), EventSourceKind.Topic);
        var topic = await context.SdkAsync(context.Reader.Sdk.ReadEventSourceAsync(
            new(source, Limit: OneEventPlusTerminalBound), token));
        var message = await context.SdkAsync(context.Reader.Sdk.InspectAsync(new(new(context.Partition,
            ResourcePartitionInventoryModelsProtocol.Resource("queue")), FunctionalGateColdProtocol.Id), token));
        await FunctionalGateColdReadAssertions.InitialHistoryAsync(context, ResourcePartitionInventoryModelsProtocol.Index,
            ResourcePartitionInventoryModelsProtocol.Document, stream, topic, message, original.Receipt.Token.Position, beforeWrite);
        return new(ResourcePartitionInventoryModelsProtocol.Index, original.Command, original.Receipt,
            table.Create, table.Definition, table.Insert, table.Receipt, blob, stream.Events.Single(),
            topic.Events.Single(), message);
    }
}
