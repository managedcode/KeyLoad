using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class SubscriptionFilterRf3Setup
{
    internal static async Task<SubscriptionFilterRf3Seed> CreateAsync(ClusterFixture fixture, CancellationToken token)
    {
        var tenant = SubscriptionFilterRf3Protocol.TenantPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var partition = new PartitionRef(tenant, SubscriptionFilterRf3Protocol.Database,
            SubscriptionFilterRf3Protocol.Domain, Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var root = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        await McpCallerAssertions.SdkSuccessAsync(await root.ConfigureResourceAsync(Guid.NewGuid(), new(tenant,
            partition.DatabaseId, new ResourceDefinition(SubscriptionFilterRf3Protocol.Topic, ResourceKind.Topic, partition.TransactionDomainId)), token));
        await McpCallerAssertions.SdkSuccessAsync(await root.ConfigureResourceAsync(Guid.NewGuid(), new(tenant,
            partition.DatabaseId, new ResourceDefinition(SubscriptionFilterRf3Protocol.Collection, ResourceKind.Collection, partition.TransactionDomainId)), token));
        var manager = await MessagingRf3Identity.CreateAsync(fixture, tenant,
            [new(partition.DatabaseId, SubscriptionFilterRf3Protocol.Topic, SubscriptionFilterRf3Protocol.Manager),
             new(partition.DatabaseId, SubscriptionFilterRf3Protocol.Collection, Capability.DocumentsRead | Capability.DocumentsWrite)], [], false, token);
        var first = await MessagingRf3Identity.CreateAsync(fixture, tenant,
            [new(partition.DatabaseId, SubscriptionFilterRf3Protocol.Topic, SubscriptionFilterRf3Protocol.Worker)], [], false, token);
        var second = await MessagingRf3Identity.CreateAsync(fixture, tenant,
            [new(partition.DatabaseId, SubscriptionFilterRf3Protocol.Topic, SubscriptionFilterRf3Protocol.Worker)], [], false, token);
        var client = new KeyLoadClient(http, manager.Secret, IntegrationClientOptions.Execution());
        var source = new EventSourceRef(partition, SubscriptionFilterRf3Protocol.Topic, EventSourceKind.Topic);
        var group = new SubscriptionRef(source, SubscriptionFilterRf3Protocol.Group);
        var independent = new SubscriptionRef(source, SubscriptionFilterRf3Protocol.Independent);
        var definition = new SubscriptionDefinition(manager.Principal.Id) { Policy = new() { MaxWindow = SubscriptionFilterRf3Protocol.Window } };
        await McpCallerAssertions.SdkSuccessAsync(await client.ConfigureSubscriptionAsync(new(Guid.NewGuid(), group, definition), token));
        await McpCallerAssertions.SdkSuccessAsync(await client.ConfigureSubscriptionAsync(new(Guid.NewGuid(), independent, definition), token));
        var publish = new CommandRequest(Guid.NewGuid(), partition,
            [new PublishTopic(SubscriptionFilterRf3Protocol.Topic,
                [new(SubscriptionFilterRf3Protocol.FirstEvent, SubscriptionFilterRf3Protocol.Created, SubscriptionFilterRf3Protocol.Payload),
                 new(SubscriptionFilterRf3Protocol.SecondEvent, SubscriptionFilterRf3Protocol.Changed, SubscriptionFilterRf3Protocol.Payload),
                 new(SubscriptionFilterRf3Protocol.ThirdEvent, SubscriptionFilterRf3Protocol.Created, SubscriptionFilterRf3Protocol.Payload),
                 new(SubscriptionFilterRf3Protocol.FourthEvent, SubscriptionFilterRf3Protocol.Changed, SubscriptionFilterRf3Protocol.Payload)])]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await client.CommitAsync(publish, token));
        return new(group, independent, manager, first, second, publish, receipt,
            new(Guid.NewGuid(), group, definition with { EventTypes = [SubscriptionFilterRf3Protocol.Changed] },
                ExpectedGeneration: SubscriptionFilterRf3Protocol.Initial));
    }
}
