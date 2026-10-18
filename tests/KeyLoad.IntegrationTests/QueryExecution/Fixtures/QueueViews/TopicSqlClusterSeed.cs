using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class TopicSqlClusterSeed
{
    internal static Task<TopicSqlClusterState> CreateAsync(ClusterFixture fixture, CancellationToken token)
        => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async callers =>
    {
        var partition = new PartitionRef(TopicSqlClusterProtocol.TenantPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat),
            TopicSqlClusterProtocol.Database, TopicSqlClusterProtocol.Domain, Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        var resource = new ResourceDefinition(TopicSqlClusterProtocol.Topic, ResourceKind.Topic, partition.TransactionDomainId)
        {
            FieldPolicies = [new(TopicSqlClusterProtocol.PayloadPath, TopicSqlClusterProtocol.Classification,
                TopicSqlClusterProtocol.ReadGrant, TopicSqlClusterProtocol.UseGrant)],
            HeaderPolicies = [new(TopicSqlClusterProtocol.HeaderPath, TopicSqlClusterProtocol.Classification,
                TopicSqlClusterProtocol.HeaderRead, TopicSqlClusterProtocol.HeaderUse)]
        };
        foreach (var definition in new[] { resource,
            new ResourceDefinition(TopicSqlClusterProtocol.Empty, ResourceKind.Topic, partition.TransactionDomainId),
            new ResourceDefinition(TopicSqlClusterProtocol.Queue, ResourceKind.WorkQueue, partition.TransactionDomainId) })
        {
            await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(partition.TenantId, partition.DatabaseId, definition), token));
        }
        var command = new CommandRequest(Guid.NewGuid(), partition,
            [new PublishTopic(TopicSqlClusterProtocol.Topic, TopicSqlClusterState.Data),
             new EnqueueMessage(TopicSqlClusterProtocol.Queue, TopicSqlClusterProtocol.Message, TopicSqlClusterProtocol.QueuePayload)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(command, token));
        var original = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadEventSourceAsync(new(new(partition,
            TopicSqlClusterProtocol.Topic, EventSourceKind.Topic)), token));
        var queue = await PublicReadAssertions.MessageAsync(await callers.Sdk.InspectAsync(new(new(partition,
            TopicSqlClusterProtocol.Queue), TopicSqlClusterProtocol.Message), token));
        var identity = await McpPersistedIdentity.CreateAsync(fixture, partition, TopicSqlClusterProtocol.Topic, Capability.Query, token);
        return new TopicSqlClusterState(partition, command, receipt, original, queue!, identity);
    }, token);
}
