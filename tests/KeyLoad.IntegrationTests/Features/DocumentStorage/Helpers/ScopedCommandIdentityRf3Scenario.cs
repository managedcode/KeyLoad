using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

/// <summary>Owns two independently named logical partitions in one real transaction domain.</summary>
internal sealed record ScopedCommandIdentityRf3Scenario(PartitionRef FirstPartition, PartitionRef SecondPartition,
    string Collection, string Queue)
{
    internal static async Task<ScopedCommandIdentityRf3Scenario> CreateAsync(ClusterFixture fixture,
        CancellationToken cancellationToken)
    {
        var tenant = McpDocumentProtocol.TenantPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var domain = "dstore-outcome-" + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var collection = "documents-" + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var queue = "queue-" + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var first = new PartitionRef(tenant, McpDocumentProtocol.Database, domain, "partition-a");
        var second = first with { PartitionKey = "partition-b" };
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var administrator = new KeyLoadClient(http, fixture.AdminKey);
        var definition = new ResourceDefinition(collection, ResourceKind.Collection, domain);
        await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureResourceAsync(Guid.NewGuid(),
            new(tenant, McpDocumentProtocol.Database, definition), cancellationToken));
        await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureResourceAsync(Guid.NewGuid(),
            new(tenant, McpDocumentProtocol.Database, new ResourceDefinition(queue, ResourceKind.WorkQueue, domain)),
            cancellationToken));
        return new(first, second, collection, queue);
    }

    internal CommandRequest CreateCommand(Guid commandId, PartitionRef partition, string entity, string json)
        => new(commandId, partition,
            [new PutDocument(Collection, entity, json), new EnqueueMessage(Queue, "same-message", json)]);
}
