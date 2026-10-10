using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class TopicSqlRf3Seed
{
    internal static Task<TopicSqlRf3State> CreateAsync(ClusterFixture fixture, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async callers =>
    {
        var partition = new PartitionRef(TopicSqlRf3Protocol.TenantPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat),
            TopicSqlRf3Protocol.Database, TopicSqlRf3Protocol.Domain, Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        var resource = new ResourceDefinition(TopicSqlRf3Protocol.Topic, ResourceKind.Topic, partition.TransactionDomainId)
        {
            FieldPolicies = [new(TopicSqlRf3Protocol.PayloadPath, TopicSqlRf3Protocol.Classification,
                TopicSqlRf3Protocol.ReadGrant, TopicSqlRf3Protocol.UseGrant)],
            HeaderPolicies = [new(TopicSqlRf3Protocol.HeaderPath, TopicSqlRf3Protocol.Classification,
                TopicSqlRf3Protocol.HeaderRead, TopicSqlRf3Protocol.HeaderUse)]
        };
        foreach (var definition in new[] { resource,
            new ResourceDefinition(TopicSqlRf3Protocol.Empty, ResourceKind.Topic, partition.TransactionDomainId),
            new ResourceDefinition(TopicSqlRf3Protocol.Queue, ResourceKind.WorkQueue, partition.TransactionDomainId) })
        { await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(partition.TenantId, partition.DatabaseId, definition), token)); }
        var command = new CommandRequest(Guid.NewGuid(), partition,
            [new PublishTopic(TopicSqlRf3Protocol.Topic, TopicSqlRf3State.Data),
             new EnqueueMessage(TopicSqlRf3Protocol.Queue, TopicSqlRf3Protocol.Message, TopicSqlRf3Protocol.QueuePayload)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(command, token));
        var original = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadEventSourceAsync(new(new(partition,
            TopicSqlRf3Protocol.Topic, EventSourceKind.Topic)), token));
        var queue = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.InspectAsync(new(new(partition,
            TopicSqlRf3Protocol.Queue), TopicSqlRf3Protocol.Message), token));
        var identity = await McpPersistedIdentity.CreateAsync(fixture, partition, TopicSqlRf3Protocol.Topic, Capability.Query, token);
        return new TopicSqlRf3State(partition, command, receipt, original, queue!, identity);
    }, token);
}
