using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal sealed record TopicRetentionRf3Scenario(PartitionRef Partition, EventSourceRef Source,
    SubscriptionRef Subscription, CommandRequest Publication, CommitReceipt Receipt)
{
    private const string PartitionKey = "one";
    internal const string RequestKey = "request";
    internal const string ArgumentsKey = "arguments";
    internal static async Task<TopicRetentionRf3Scenario> CreateAsync(KeyLoadClient sdk, CancellationToken token)
    {
        var partition = new PartitionRef("retention-" + Guid.NewGuid().ToString("N"), "database", "orders", PartitionKey);
        foreach (var item in new[] { ("topic", ResourceKind.Topic), ("orders", ResourceKind.Collection), ("work", ResourceKind.WorkQueue) })
        {
            await McpCallerAssertions.SdkSuccessAsync(await sdk.ConfigureResourceAsync(Guid.NewGuid(),
                new(partition.TenantId, partition.DatabaseId, new(item.Item1, item.Item2, partition.TransactionDomainId)), token));
        }
        var publication = new CommandRequest(Guid.NewGuid(), partition,
            [new PutDocument("orders", "derived", "{\"done\":true}"), new EnqueueMessage("work", "derived", "{\"input\":1}"),
             new PublishTopic("topic", [new("event1", "Created", "{\"n\":1}"), new("event2", "Created", "{\"n\":2}"),
                 new("event3", "Created", "{\"n\":3}")])]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(publication, token));
        var source = new EventSourceRef(partition, "topic", EventSourceKind.Topic);
        var subscription = new SubscriptionRef(source, "worker");
        await McpCallerAssertions.SdkSuccessAsync(await sdk.ConfigureSubscriptionAsync(
            new(Guid.NewGuid(), subscription, new("root")), token));
        return new(partition, source, subscription, publication, receipt);
    }

    internal CommandRequest Purge() => new(Guid.NewGuid(), Partition, [new PurgeTopic("topic", 2)]);

    internal SqlOperationRequest HealthySql()
    {
        var command = new CommandRequest(Guid.NewGuid(), Partition,
            [new PublishTopic("topic", [new("event4", "Created", "{\"n\":4}")])]);
        return new(Partition, "CALL keyload_documents_commit(@arguments)",
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            { [ArgumentsKey] = JsonSerializer.SerializeToElement(new { request = command }, JsonDefaults.Options) });
    }
}
