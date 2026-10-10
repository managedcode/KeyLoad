using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class InboxProcessingRf3Seed
{
    internal static async Task<(QueueLaneRef Lane, MessagingRf3Identity Identity)> CreateAsync(
        ClusterFixture fixture, KeyLoadClient administrator, CancellationToken token)
    {
        var tenant = InboxProcessingRf3Protocol.Tenant + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var partition = new PartitionRef(tenant, InboxProcessingRf3Protocol.Database, InboxProcessingRf3Protocol.Domain,
            Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        foreach (var name in new[] { InboxProcessingRf3Protocol.Input, InboxProcessingRf3Protocol.Output })
        {
            _ = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureResourceAsync(Guid.NewGuid(),
                new(tenant, partition.DatabaseId, new(name, ResourceKind.WorkQueue, partition.TransactionDomainId)), token));
        }
        _ = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureResourceAsync(Guid.NewGuid(),
            new(tenant, partition.DatabaseId, new(InboxProcessingRf3Protocol.Collection, ResourceKind.Collection,
                partition.TransactionDomainId)), token));
        var identity = await MessagingRf3Identity.CreateAsync(fixture, tenant,
            [new(partition.DatabaseId, InboxProcessingRf3Protocol.Input, Capability.QueueConsume | Capability.QueueAck | Capability.QueueInspect),
             new(partition.DatabaseId, InboxProcessingRf3Protocol.Output, Capability.QueuePublish | Capability.QueueConsume | Capability.QueueAck | Capability.QueueInspect),
             new(partition.DatabaseId, InboxProcessingRf3Protocol.Collection, Capability.DocumentsRead | Capability.DocumentsWrite)], [], false, token);
        _ = await McpCallerAssertions.SdkSuccessAsync(await administrator.CommitAsync(new(Guid.NewGuid(), partition,
            [new EnqueueMessage(InboxProcessingRf3Protocol.Input, InboxProcessingRf3Protocol.Message, InboxProcessingRf3Protocol.Payload)]), token));
        return (new(partition, InboxProcessingRf3Protocol.Input), identity);
    }
}
