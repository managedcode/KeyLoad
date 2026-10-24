using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.ResourceExecution;
namespace KeyLoad.IntegrationTests.Messaging;

internal static class InboxProcessingClusterSeed
{
    internal static async Task<(QueueLaneRef Lane, MessagingClusterIdentity Identity, EntityRefLifetime CollectionLifetime)> CreateAsync(
        ClusterFixture fixture, KeyLoadClient administrator, CancellationToken token)
    {
        var tenant = InboxProcessingClusterProtocol.Tenant + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var partition = new PartitionRef(tenant, InboxProcessingClusterProtocol.Database, InboxProcessingClusterProtocol.Domain,
            Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        foreach (var name in new[] { InboxProcessingClusterProtocol.Input, InboxProcessingClusterProtocol.Output })
        {
            _ = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureResourceAsync(Guid.NewGuid(),
                new(tenant, partition.DatabaseId, new(name, ResourceKind.WorkQueue, partition.TransactionDomainId)), token));
        }
        var collectionId = Guid.NewGuid();
        var collection = new ConfigureResourceRequest(tenant, partition.DatabaseId, new(InboxProcessingClusterProtocol.Collection,
            ResourceKind.Collection, partition.TransactionDomainId));
        _ = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureResourceAsync(collectionId, collection, token));
        var identity = await MessagingClusterIdentity.CreateAsync(fixture, tenant,
            [new(partition.DatabaseId, InboxProcessingClusterProtocol.Input, Capability.QueueConsume | Capability.QueueAck | Capability.QueueInspect),
             new(partition.DatabaseId, InboxProcessingClusterProtocol.Output, Capability.QueuePublish | Capability.QueueConsume | Capability.QueueAck | Capability.QueueInspect),
             new(partition.DatabaseId, InboxProcessingClusterProtocol.Collection, Capability.DocumentsRead | Capability.DocumentsWrite)], [], false, token);
        _ = await McpCallerAssertions.SdkSuccessAsync(await administrator.CommitAsync(new(Guid.NewGuid(), partition,
            [new EnqueueMessage(InboxProcessingClusterProtocol.Input, InboxProcessingClusterProtocol.Message, InboxProcessingClusterProtocol.Payload)]), token));
        return (new(partition, InboxProcessingClusterProtocol.Input), identity, ResourceCreationLifetime.Create(collectionId, collection));
    }
}
