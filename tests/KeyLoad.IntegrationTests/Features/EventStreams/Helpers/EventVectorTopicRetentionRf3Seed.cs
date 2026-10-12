using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal sealed record EventVectorTopicRetentionRf3Seed(string Node, EventSourceRef Source,
    CommandRequest Publication, CommitReceipt Receipt, CommandRequest Refused,
    DocumentResult Document, MessageInspection Message)
{
    internal const string Topic = "vector-retention-topic";
    internal const string Documents = "vector-retention-documents";
    internal const string Queue = "vector-retention-work";
    internal const string Item = "protected-original";
    internal static EventData[] Events() => [new("first", "Created", "{\"n\":1}"),
        new("second", "Created", "{\"n\":2}"), new("third", "Created", "{\"n\":3}")];

    internal static Task<EventVectorTopicRetentionRf3Seed> CreateAsync(TwoRf3MembershipWave wave,
        string credential, string node, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync<EventVectorTopicRetentionRf3Seed>(wave.Application, node, credential, async callers =>
        {
            var partition = new PartitionRef("protected-parent-tenant", "vector-retention-database",
                "vector-retention-domain", Guid.NewGuid().ToString("N"));
            foreach (var value in new[] { (Topic, ResourceKind.Topic), (Documents, ResourceKind.Collection),
                (Queue, ResourceKind.WorkQueue) })
            {
                _ = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
                    new(partition.TenantId, partition.DatabaseId,
                        new(value.Item1, value.Item2, partition.TransactionDomainId)), token));
            }
            var publication = new CommandRequest(Guid.NewGuid(), partition,
                [new PutDocument(Documents, Item, "{\"original\":true}"),
                 new EnqueueMessage(Queue, Item, "{\"original\":true}"), new PublishTopic(Topic, [.. Events()])]);
            var receipt = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(publication, token));
            await SqlRf3Protocol.EqualAsync(receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await callers.Mcp.CallAsync(McpCallerTools.DocumentsCommit, publication, token))).Value);
            var reference = new EntityRef(partition, Documents, Item);
            var document = await PublicReadAssertions.DocumentAsync(await callers.Sdk.GetAsync(reference, token));
            await SqlRf3Protocol.EqualAsync(new DocumentResult(reference, 1, "{\"original\":true}", false, []), document);
            var message = await PublicReadAssertions.MessageAsync(await callers.Sdk.InspectAsync(
                new(new(partition, Queue), Item), token));
            await Assert.That(message).IsNotNull();
            await Assert.That(message!.Metadata.State).IsEqualTo(MessageState.Ready);
            await Assert.That(message.Metadata.Attempts).IsEqualTo(0);
            await Assert.That(message.PayloadJson).IsEqualTo("{\"original\":true}");
            return new(node, new(partition, Topic, EventSourceKind.Topic), publication, receipt,
                new(Guid.NewGuid(), partition, [new PurgeTopic(Topic, 2)]), document!, message);
        }, token);
}
