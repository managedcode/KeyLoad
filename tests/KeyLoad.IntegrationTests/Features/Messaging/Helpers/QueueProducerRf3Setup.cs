using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueProducerRf3Setup
{
    internal static async Task<QueueProducerRf3Seed> CreateAsync(ClusterFixture fixture, CancellationToken token)
    {
        var tenant = QueueProducerRf3Protocol.TenantPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var partition = new PartitionRef(tenant, QueueProducerRf3Protocol.Database, QueueProducerRf3Protocol.Domain,
            Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        var lane = new QueueLaneRef(partition, QueueProducerRf3Protocol.Queue);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var root = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        await McpCallerAssertions.SdkSuccessAsync(await root.ConfigureResourceAsync(Guid.NewGuid(),
            new(tenant, partition.DatabaseId, new ResourceDefinition(lane.Queue, ResourceKind.WorkQueue,
                partition.TransactionDomainId)
            { QueuePolicy = new() { MaxStoredMessages = QueueProducerRf3Protocol.StoredMessageCapacity } }), token));
        await McpCallerAssertions.SdkSuccessAsync(await root.ConfigureResourceAsync(Guid.NewGuid(),
            new(tenant, partition.DatabaseId, new ResourceDefinition(QueueProducerRf3Protocol.Collection,
                ResourceKind.Collection, partition.TransactionDomainId)), token));
        var identity = await MessagingRf3Identity.CreateAsync(fixture, tenant,
            [new(partition.DatabaseId, lane.Queue, QueueProducerRf3Protocol.Publisher),
             new(partition.DatabaseId, QueueProducerRf3Protocol.Collection, Capability.DocumentsRead | Capability.DocumentsWrite)], [], false, token);
        var due = TimeProvider.System.GetUtcNow().AddHours(QueueProducerRf3Protocol.FutureHours);
        var original = new CommandRequest(Guid.NewGuid(), partition,
            [new PutDocument(QueueProducerRf3Protocol.Collection, QueueProducerRf3Protocol.Original, QueueProducerRf3Protocol.Payload),
             new EnqueueMessage(lane.Queue, QueueProducerRf3Protocol.Original, QueueProducerRf3Protocol.Payload, QueueProducerRf3Protocol.Headers),
             new EnqueueMessage(lane.Queue, QueueProducerRf3Protocol.Scheduled, QueueProducerRf3Protocol.Payload, QueueProducerRf3Protocol.Headers, NotBefore: due)]);
        return new(lane, identity, original, due);
    }
    internal static CommandRequest Fresh(QueueProducerRf3Seed seed, string document, string message)
        => new(Guid.NewGuid(), seed.Lane.Partition,
            [new PutDocument(QueueProducerRf3Protocol.Collection, document, QueueProducerRf3Protocol.HealthyPayload),
             new EnqueueMessage(seed.Lane.Queue, message, QueueProducerRf3Protocol.HealthyPayload, QueueProducerRf3Protocol.Headers)]);
}
