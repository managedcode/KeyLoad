using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class QueryCapabilityVersionClusterSetup
{
    internal static async Task<QueryCapabilityVersionClusterState> RunAsync(ClusterFixture fixture, RequestCqrsClusterCallers root, CancellationToken token)
    {
        var partition = new PartitionRef("query-versions-" + Guid.NewGuid().ToString("N"), "database", "domain", Guid.NewGuid().ToString("N"));
        var definition = new ResourceDefinition(QueryCapabilityVersionClusterRequests.Collection, ResourceKind.Collection, partition.TransactionDomainId);
        await SqlClusterProtocol.EqualAsync(definition, await SqlClusterProtocol.SdkAsync<ResourceDefinition>(root.Sdk,
            SqlClusterProtocol.Call(partition, McpCallerTools.ResourcesConfigure,
                new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, definition), Guid.NewGuid()), token));
        var seed = QueryCapabilityVersionClusterRequests.Seed(partition);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(seed, token));
        var reader = await McpPersistedIdentity.CreateAsync(fixture, partition, QueryCapabilityVersionClusterRequests.Collection,
            Capability.Query | Capability.DocumentsRead, token);
        var outbox = await QueryCursorRequestIdentityR47Current.OutboxAsync(root, partition, token);
        await Assert.That(outbox.Head.Tail).IsEqualTo(4L);
        await Assert.That(outbox.Head.FirstAvailable).IsEqualTo(1L);
        await Assert.That(outbox.Head.StoredRecords).IsEqualTo(4L);
        await Assert.That(outbox.Head.StoredBytes).IsGreaterThan(0L);
        await Assert.That(outbox.Consumers).IsEmpty();
        var state = new QueryCapabilityVersionClusterState(seed, receipt, reader, outbox);
        await QueryCapabilityVersionClusterCurrent.CheckAsync(root, state, token);
        return state;
    }
    internal static async Task PolicyAsync(RequestCqrsClusterCallers root, QueryCapabilityVersionClusterState state, long epoch, bool allow, CancellationToken token)
    {
        var expected = state.Reader.Principal with
        {
            PolicyEpoch = epoch,
            Grants = allow ? [new(state.Partition.DatabaseId, QueryCapabilityVersionClusterRequests.Collection, Capability.Query | Capability.DocumentsRead)] : []
        };
        await SqlClusterProtocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token)));
    }
}
