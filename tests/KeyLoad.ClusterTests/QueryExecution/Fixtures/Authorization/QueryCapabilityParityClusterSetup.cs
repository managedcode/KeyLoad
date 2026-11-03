using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.ResourceExecution;

namespace KeyLoad.ClusterTests.QueryExecution;

internal static class QueryCapabilityParityClusterSetup
{
    internal static async Task<QueryCapabilityParityClusterState> RunAsync(ClusterFixture fixture, RequestCqrsClusterCallers root, CancellationToken token)
    {
        var partition = new PartitionRef("query-parity-" + Guid.NewGuid().ToString("N"), "database", "domain", Guid.NewGuid().ToString("N"));
        var definition = new ResourceDefinition(QueryCapabilityParityClusterRequests.Collection, ResourceKind.Collection, partition.TransactionDomainId);
        var creationId = Guid.NewGuid();
        var creation = new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, definition);
        await SqlClusterProtocol.EqualAsync(definition, await SqlClusterProtocol.SdkAsync<ResourceDefinition>(root.Sdk,
            SqlClusterProtocol.Call(partition, McpCallerTools.ResourcesConfigure, creation, creationId), token));
        var seed = QueryCapabilityParityClusterRequests.Seed(partition);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(seed, token));
        var reader = await McpPersistedIdentity.CreateAsync(fixture, partition, QueryCapabilityParityClusterRequests.Collection,
            Capability.Query | Capability.DocumentsRead, token);
        var outbox = await QueryCursorRequestIdentityR47Current.OutboxAsync(root, partition, token);
        await Assert.That(outbox.Head.Tail).IsEqualTo(4L);
        await Assert.That(outbox.Head.FirstAvailable).IsEqualTo(1L);
        await Assert.That(outbox.Head.StoredRecords).IsEqualTo(4L);
        await Assert.That(outbox.Head.StoredBytes).IsGreaterThan(0L);
        await Assert.That(outbox.Consumers).IsEmpty();
        var state = new QueryCapabilityParityClusterState(seed, receipt, reader, outbox, ResourceCreationLifetime.Create(creationId, creation));
        await QueryCapabilityParityClusterCurrent.CheckAsync(root, state, token);
        return state;
    }
    internal static async Task PolicyAsync(RequestCqrsClusterCallers root, QueryCapabilityParityClusterState state, long epoch, bool allow, CancellationToken token)
    {
        var expected = state.Reader.Principal with
        {
            PolicyEpoch = epoch,
            Grants = allow ? [new(state.Partition.DatabaseId, QueryCapabilityParityClusterRequests.Collection, Capability.Query | Capability.DocumentsRead)] : []
        };
        await SqlClusterProtocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token)));
    }
}
