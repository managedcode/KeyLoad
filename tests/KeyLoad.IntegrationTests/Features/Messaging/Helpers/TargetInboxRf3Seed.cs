using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class TargetInboxRf3Seed
{
    internal static async Task<(QueueLaneRef Source, QueueLaneRef Target, MessagingRf3Identity Identity)> CreateAsync(
        ClusterFixture fixture, KeyLoadClient administrator, CancellationToken token)
    {
        var tenant = TargetInboxRf3Protocol.Tenant + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var source = new PartitionRef(tenant, TargetInboxRf3Protocol.Database, TargetInboxRf3Protocol.Domain, TargetInboxRf3Protocol.SourcePartition);
        var target = source with { PartitionKey = TargetInboxRf3Protocol.TargetPartition };
        foreach (var name in new[] { TargetInboxRf3Protocol.Input, TargetInboxRf3Protocol.Output })
        {
            _ = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureResourceAsync(Guid.NewGuid(),
            new(tenant, source.DatabaseId, new(name, ResourceKind.WorkQueue, source.TransactionDomainId)), token));
        }
        _ = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureResourceAsync(Guid.NewGuid(),
            new(tenant, target.DatabaseId, new(TargetInboxRf3Protocol.Target, ResourceKind.WorkQueue, target.TransactionDomainId)
            { InboxPolicy = new(TargetInboxRf3Protocol.ReceiptCapacity, TargetInboxRf3Protocol.ByteCapacity) }), token));
        _ = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureResourceAsync(Guid.NewGuid(),
            new(tenant, target.DatabaseId, new(TargetInboxRf3Protocol.Collection, ResourceKind.Collection, target.TransactionDomainId)), token));
        var identity = await MessagingRf3Identity.CreateAsync(fixture, tenant,
            [new(target.DatabaseId, TargetInboxRf3Protocol.Target, Capability.InboxWrite),
             new(source.DatabaseId, TargetInboxRf3Protocol.Input, Capability.QueueConsume | Capability.QueueAck | Capability.QueueInspect),
             new(target.DatabaseId, TargetInboxRf3Protocol.Output, Capability.QueuePublish | Capability.QueueConsume | Capability.QueueAck | Capability.QueueInspect),
             new(target.DatabaseId, TargetInboxRf3Protocol.Collection, Capability.DocumentsRead | Capability.DocumentsWrite)], [], false, token);
        _ = await McpCallerAssertions.SdkSuccessAsync(await administrator.CommitAsync(new(Guid.NewGuid(), source,
            [new EnqueueMessage(TargetInboxRf3Protocol.Input, TargetInboxRf3Protocol.Message, TargetInboxRf3Protocol.Payload)]), token));
        return (new(source, TargetInboxRf3Protocol.Input), new(target, TargetInboxRf3Protocol.Target), identity);
    }
}
