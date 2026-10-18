using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueProducerClusterSetup
{
    internal static async Task<QueueProducerClusterSeed> CreateAsync(ClusterFixture fixture, CancellationToken token)
    {
        var tenant = QueueProducerClusterProtocol.TenantPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var partition = new PartitionRef(tenant, QueueProducerClusterProtocol.Database, QueueProducerClusterProtocol.Domain,
            Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        var lane = new QueueLaneRef(partition, QueueProducerClusterProtocol.Queue);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var root = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        await McpCallerAssertions.SdkSuccessAsync(await root.ConfigureResourceAsync(Guid.NewGuid(),
            new(tenant, partition.DatabaseId, new ResourceDefinition(lane.Queue, ResourceKind.WorkQueue,
                partition.TransactionDomainId)
            {
                QueuePolicy = new() { MaxStoredMessages = QueueProducerClusterProtocol.StoredMessageCapacity }
            }), token));
        await McpCallerAssertions.SdkSuccessAsync(await root.ConfigureResourceAsync(Guid.NewGuid(),
            new(tenant, partition.DatabaseId, new ResourceDefinition(QueueProducerClusterProtocol.Collection,
                ResourceKind.Collection, partition.TransactionDomainId)
            {
                Indexes = [new(AtomicProducerClusterIndex.Name, [AtomicProducerClusterIndex.Field], true)]
            }), token));
        foreach (var resource in new[]
        { new ResourceDefinition(QueueProducerClusterProtocol.StreamSet, ResourceKind.StreamSet, partition.TransactionDomainId),
          new ResourceDefinition(QueueProducerClusterProtocol.ForeignStreamSet, ResourceKind.StreamSet, QueueProducerClusterProtocol.ForeignDomain) })
        {
            await McpCallerAssertions.SdkSuccessAsync(await root.ConfigureResourceAsync(Guid.NewGuid(),
            new(tenant, partition.DatabaseId, resource), token));
        }
        var identity = await MessagingClusterIdentity.CreateAsync(fixture, tenant,
            [new(partition.DatabaseId, lane.Queue, QueueProducerClusterProtocol.Publisher),
             new(partition.DatabaseId, QueueProducerClusterProtocol.Collection, Capability.DocumentsRead | Capability.DocumentsWrite | Capability.Query),
             new(partition.DatabaseId, QueueProducerClusterProtocol.StreamSet, Capability.EventsRead | Capability.EventsAppend | Capability.Query),
             new(partition.DatabaseId, QueueProducerClusterProtocol.ForeignStreamSet, Capability.EventsRead | Capability.EventsAppend | Capability.Query)], [], false, token);
        var due = TimeProvider.System.GetUtcNow().AddHours(QueueProducerClusterProtocol.FutureHours);
        var original = new CommandRequest(Guid.NewGuid(), partition,
            [new PutDocument(QueueProducerClusterProtocol.Collection, QueueProducerClusterProtocol.Original, QueueProducerClusterProtocol.Payload),
             Event(QueueProducerClusterProtocol.Original, QueueProducerClusterProtocol.Payload),
             new EnqueueMessage(lane.Queue, QueueProducerClusterProtocol.Original, QueueProducerClusterProtocol.Payload, QueueProducerClusterProtocol.Headers),
             new EnqueueMessage(lane.Queue, QueueProducerClusterProtocol.Scheduled, QueueProducerClusterProtocol.Payload, QueueProducerClusterProtocol.Headers, NotBefore: due)]);
        return new(lane, identity, original, due);
    }
    internal static CommandRequest Fresh(QueueProducerClusterSeed seed, string document, string message)
        => new(Guid.NewGuid(), seed.Lane.Partition,
            [new PutDocument(QueueProducerClusterProtocol.Collection, document, QueueProducerClusterProtocol.HealthyPayload),
             Event(document, QueueProducerClusterProtocol.HealthyPayload),
             new EnqueueMessage(seed.Lane.Queue, message, QueueProducerClusterProtocol.HealthyPayload, QueueProducerClusterProtocol.Headers)]);
    internal static AppendEvents Event(string id, string payload)
        => new(QueueProducerClusterProtocol.StreamSet, id,
            [new(id, QueueProducerClusterProtocol.EventType, payload, QueueProducerClusterProtocol.Headers)], ExpectedStreamRevision.NoStream);
}
