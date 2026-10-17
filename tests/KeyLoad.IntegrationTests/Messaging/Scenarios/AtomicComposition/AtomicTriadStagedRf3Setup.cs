using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class AtomicTriadStagedRf3Setup
{
    internal static async Task<AtomicTriadStagedRf3State> RunAsync(ClusterFixture fixture, RequestCqrsRf3Callers root, CancellationToken token)
    {
        var tenant = AtomicTriadStagedRf3Protocol.TenantPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var partition = new PartitionRef(tenant, AtomicTriadStagedRf3Protocol.Database, AtomicTriadStagedRf3Protocol.Domain,
            Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        ResourceDefinition[] resources =
        [new(AtomicTriadStagedRf3Protocol.Collection, ResourceKind.Collection, partition.TransactionDomainId)
        {
            Indexes = [new(AtomicTriadStagedRf3Protocol.Index, [AtomicTriadStagedRf3Protocol.IndexField], true)]
        },
         new(AtomicTriadStagedRf3Protocol.Streams, ResourceKind.StreamSet, partition.TransactionDomainId),
         new(AtomicTriadStagedRf3Protocol.Queue, ResourceKind.WorkQueue, partition.TransactionDomainId)];
        foreach (var resource in resources)
        {
            await QueueProducerRf3Assertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigureResourceAsync(
                Guid.NewGuid(), new(tenant, partition.DatabaseId, resource), token)), resource);
        }
        var identity = await MessagingRf3Identity.CreateAsync(fixture, tenant,
            [new(partition.DatabaseId, AtomicTriadStagedRf3Protocol.Collection, Capability.DocumentsRead | Capability.DocumentsWrite | Capability.Query),
             new(partition.DatabaseId, AtomicTriadStagedRf3Protocol.Streams, Capability.EventsRead | Capability.EventsAppend),
             new(partition.DatabaseId, AtomicTriadStagedRf3Protocol.Queue, Capability.QueuePublish | Capability.QueueInspect)], [], false, token);
        return new(partition, identity, AtomicTriadStagedRf3Recipe.Command(partition, true));
    }

    internal static Task PolicyAsync(ClusterFixture fixture, AtomicTriadStagedRf3State state, long epoch, bool publish, CancellationToken token)
        => MessagingRf3Identity.UpdateAsync(fixture, state.Identity.Principal with
        {
            PolicyEpoch = epoch,
            Grants = [.. state.Identity.Principal.Grants.Select(grant => grant.Resource == AtomicTriadStagedRf3Protocol.Queue
                ? grant with
                {
                    Capabilities = Capability.QueueInspect | (publish ? Capability.QueuePublish : Capability.None)
                } : grant)]
        }, token);
}
