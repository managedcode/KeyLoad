using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.ResourceExecution;

namespace KeyLoad.ClusterTests.DocumentStorage;

/// <summary>Owns two independently named logical partitions in one real transaction domain.</summary>
internal sealed record ScopedCommandIdentityClusterScenario(PartitionRef FirstPartition, PartitionRef SecondPartition,
    string Collection, string Queue, EntityRefLifetime Lifetime)
{
    private const string FirstPartitionKey = "partition-a";
    internal static async Task<ScopedCommandIdentityClusterScenario> CreateAsync(ClusterFixture fixture,
        CancellationToken cancellationToken)
    {
        var tenant = McpDocumentProtocol.TenantPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var domain = "dstore-outcome-" + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var collection = "documents-" + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var queue = "queue-" + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var first = new PartitionRef(tenant, McpDocumentProtocol.Database, domain, FirstPartitionKey);
        var second = first with
        {
            PartitionKey = "partition-b"
        };
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var administrator = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var definition = new ResourceDefinition(collection, ResourceKind.Collection, domain);
        var creationId = Guid.NewGuid();
        var creation = new ConfigureResourceRequest(tenant, McpDocumentProtocol.Database, definition);
        await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureResourceAsync(creationId, creation, cancellationToken));
        await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureResourceAsync(Guid.NewGuid(),
            new(tenant, McpDocumentProtocol.Database, new ResourceDefinition(queue, ResourceKind.WorkQueue, domain)),
            cancellationToken));
        return new(first, second, collection, queue, ResourceCreationLifetime.Create(creationId, creation));
    }

    internal CommandRequest CreateCommand(Guid commandId, PartitionRef partition, string entity, string json)
        => new(commandId, partition,
            [new PutDocument(Collection, entity, json), new EnqueueMessage(Queue, "same-message", json)]);
}
