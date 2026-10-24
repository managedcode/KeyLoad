using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.ResourceExecution;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class AtomicTriadStagedClusterSetup
{
    internal static async Task<AtomicTriadStagedClusterState> RunAsync(ClusterFixture fixture, RequestCqrsClusterCallers root, CancellationToken token)
    {
        var tenant = AtomicTriadStagedClusterProtocol.TenantPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var partition = new PartitionRef(tenant, AtomicTriadStagedClusterProtocol.Database, AtomicTriadStagedClusterProtocol.Domain,
            Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        ResourceDefinition[] resources =
        [new(AtomicTriadStagedClusterProtocol.Collection, ResourceKind.Collection, partition.TransactionDomainId)
        {
            Indexes = [new(AtomicTriadStagedClusterProtocol.Index, [AtomicTriadStagedClusterProtocol.IndexField], true)]
        },
         new(AtomicTriadStagedClusterProtocol.Streams, ResourceKind.StreamSet, partition.TransactionDomainId),
         new(AtomicTriadStagedClusterProtocol.Queue, ResourceKind.WorkQueue, partition.TransactionDomainId)];
        var lifetimes = new Dictionary<string, EntityRefLifetime>(StringComparer.Ordinal);
        foreach (var resource in resources)
        {
            var creationId = Guid.NewGuid();
            var creation = new ConfigureResourceRequest(tenant, partition.DatabaseId, resource);
            await QueueProducerClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigureResourceAsync(
                creationId, creation, token)), resource);
            lifetimes.Add(resource.Name, ResourceCreationLifetime.Create(creationId, creation));
        }
        var identity = await MessagingClusterIdentity.CreateAsync(fixture, tenant,
            [new(partition.DatabaseId, AtomicTriadStagedClusterProtocol.Collection, Capability.DocumentsRead | Capability.DocumentsWrite | Capability.Query),
             new(partition.DatabaseId, AtomicTriadStagedClusterProtocol.Streams, Capability.EventsRead | Capability.EventsAppend),
             new(partition.DatabaseId, AtomicTriadStagedClusterProtocol.Queue, Capability.QueuePublish | Capability.QueueInspect)], [], false, token);
        return new(partition, identity, AtomicTriadStagedClusterRecipe.Command(partition, true),
            lifetimes[AtomicTriadStagedClusterProtocol.Collection]);
    }

    internal static Task PolicyAsync(ClusterFixture fixture, AtomicTriadStagedClusterState state, long epoch, bool publish, CancellationToken token)
        => MessagingClusterIdentity.UpdateAsync(fixture, state.Identity.Principal with
        {
            PolicyEpoch = epoch,
            Grants = [.. state.Identity.Principal.Grants.Select(grant => grant.Resource == AtomicTriadStagedClusterProtocol.Queue
                ? grant with
                {
                    Capabilities = Capability.QueueInspect | (publish ? Capability.QueuePublish : Capability.None)
                } : grant)]
        }, token);
}
