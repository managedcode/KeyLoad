using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.Messaging;

internal static class SubscriptionFilterClusterSetup
{
    internal static async Task<SubscriptionFilterClusterSeed> CreateAsync(ClusterFixture fixture, CancellationToken token)
    {
        var tenant = SubscriptionFilterClusterProtocol.TenantPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var partition = new PartitionRef(tenant, SubscriptionFilterClusterProtocol.Database,
            SubscriptionFilterClusterProtocol.Domain, Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var root = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        await McpCallerAssertions.SdkSuccessAsync(await root.ConfigureResourceAsync(Guid.NewGuid(), new(tenant,
            partition.DatabaseId, new ResourceDefinition(SubscriptionFilterClusterProtocol.Topic, ResourceKind.Topic, partition.TransactionDomainId)), token));
        await McpCallerAssertions.SdkSuccessAsync(await root.ConfigureResourceAsync(Guid.NewGuid(), new(tenant,
            partition.DatabaseId, new ResourceDefinition(SubscriptionFilterClusterProtocol.Collection, ResourceKind.Collection, partition.TransactionDomainId)), token));
        var manager = await MessagingClusterIdentity.CreateAsync(fixture, tenant,
            [new(partition.DatabaseId, SubscriptionFilterClusterProtocol.Topic, SubscriptionFilterClusterProtocol.Manager),
             new(partition.DatabaseId, SubscriptionFilterClusterProtocol.Collection, Capability.DocumentsRead | Capability.DocumentsWrite)], [], false, token);
        var first = await MessagingClusterIdentity.CreateAsync(fixture, tenant,
            [new(partition.DatabaseId, SubscriptionFilterClusterProtocol.Topic, SubscriptionFilterClusterProtocol.Worker)], [], false, token);
        var second = await MessagingClusterIdentity.CreateAsync(fixture, tenant,
            [new(partition.DatabaseId, SubscriptionFilterClusterProtocol.Topic, SubscriptionFilterClusterProtocol.Worker)], [], false, token);
        var client = new KeyLoadClient(http, manager.Secret, IntegrationClientOptions.Execution());
        var source = new EventSourceRef(partition, SubscriptionFilterClusterProtocol.Topic, EventSourceKind.Topic);
        var group = new SubscriptionRef(source, SubscriptionFilterClusterProtocol.Group);
        var independent = new SubscriptionRef(source, SubscriptionFilterClusterProtocol.Independent);
        var definition = new SubscriptionDefinition(manager.Principal.Id) { Policy = new() { MaxWindow = SubscriptionFilterClusterProtocol.Window } };
        await McpCallerAssertions.SdkSuccessAsync(await client.ConfigureSubscriptionAsync(new(Guid.NewGuid(), group, definition), token));
        await McpCallerAssertions.SdkSuccessAsync(await client.ConfigureSubscriptionAsync(new(Guid.NewGuid(), independent, definition), token));
        var publish = new CommandRequest(Guid.NewGuid(), partition,
            [new PublishTopic(SubscriptionFilterClusterProtocol.Topic,
                [new(SubscriptionFilterClusterProtocol.FirstEvent, SubscriptionFilterClusterProtocol.Created, SubscriptionFilterClusterProtocol.Payload),
                 new(SubscriptionFilterClusterProtocol.SecondEvent, SubscriptionFilterClusterProtocol.Changed, SubscriptionFilterClusterProtocol.Payload),
                 new(SubscriptionFilterClusterProtocol.ThirdEvent, SubscriptionFilterClusterProtocol.Created, SubscriptionFilterClusterProtocol.Payload),
                 new(SubscriptionFilterClusterProtocol.FourthEvent, SubscriptionFilterClusterProtocol.Changed, SubscriptionFilterClusterProtocol.Payload)])]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await client.CommitAsync(publish, token));
        return new(group, independent, manager, first, second, publish, receipt,
            new(Guid.NewGuid(), group, definition with
            {
                EventTypes = [SubscriptionFilterClusterProtocol.Changed]
            },
                ExpectedGeneration: SubscriptionFilterClusterProtocol.Initial));
    }
}
