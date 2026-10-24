using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
namespace KeyLoad.IntegrationTests.Messaging;

internal static class TargetInboxClusterSeed
{
    internal static async Task<(QueueLaneRef Source, QueueLaneRef Target, MessagingClusterIdentity Identity)> CreateAsync(
        ClusterFixture fixture, KeyLoadClient administrator, CancellationToken token)
    {
        var tenant = TargetInboxClusterProtocol.Tenant + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var source = new PartitionRef(tenant, TargetInboxClusterProtocol.Database, TargetInboxClusterProtocol.Domain, TargetInboxClusterProtocol.SourcePartition);
        var target = source with
        {
            PartitionKey = TargetInboxClusterProtocol.TargetPartition
        };
        foreach (var name in new[] { TargetInboxClusterProtocol.Input, TargetInboxClusterProtocol.Output })
        {
            _ = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureResourceAsync(Guid.NewGuid(),
            new(tenant, source.DatabaseId, new(name, ResourceKind.WorkQueue, source.TransactionDomainId)), token));
        }
        _ = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureResourceAsync(Guid.NewGuid(),
            new(tenant, target.DatabaseId, new(TargetInboxClusterProtocol.Target, ResourceKind.WorkQueue, target.TransactionDomainId)
            {
                InboxPolicy = new(TargetInboxClusterProtocol.ReceiptCapacity, TargetInboxClusterProtocol.ByteCapacity)
            }), token));
        _ = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureResourceAsync(Guid.NewGuid(),
            new(tenant, target.DatabaseId, new(TargetInboxClusterProtocol.Collection, ResourceKind.Collection, target.TransactionDomainId)), token));
        var identity = await MessagingClusterIdentity.CreateAsync(fixture, tenant,
            [new(target.DatabaseId, TargetInboxClusterProtocol.Target, Capability.InboxWrite),
             new(source.DatabaseId, TargetInboxClusterProtocol.Input, Capability.QueueConsume | Capability.QueueAck | Capability.QueueInspect),
             new(target.DatabaseId, TargetInboxClusterProtocol.Output, Capability.QueuePublish | Capability.QueueConsume | Capability.QueueAck | Capability.QueueInspect),
             new(target.DatabaseId, TargetInboxClusterProtocol.Collection, Capability.DocumentsRead | Capability.DocumentsWrite)], [], false, token);
        _ = await McpCallerAssertions.SdkSuccessAsync(await administrator.CommitAsync(new(Guid.NewGuid(), source,
            [new EnqueueMessage(TargetInboxClusterProtocol.Input, TargetInboxClusterProtocol.Message, TargetInboxClusterProtocol.Payload)]), token));
        return (new(source, TargetInboxClusterProtocol.Input), new(target, TargetInboxClusterProtocol.Target), identity);
    }
}
