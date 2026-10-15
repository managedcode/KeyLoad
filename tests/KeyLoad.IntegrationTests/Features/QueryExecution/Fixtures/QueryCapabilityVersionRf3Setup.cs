using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class QueryCapabilityVersionRf3Setup
{
    internal static async Task<QueryCapabilityVersionRf3State> RunAsync(ClusterFixture fixture, RequestCqrsRf3Callers root, CancellationToken token)
    {
        var partition = new PartitionRef("query-versions-" + Guid.NewGuid().ToString("N"), "database", "domain", Guid.NewGuid().ToString("N"));
        var definition = new ResourceDefinition(QueryCapabilityVersionRf3Requests.Collection, ResourceKind.Collection, partition.TransactionDomainId);
        await SqlRf3Protocol.EqualAsync(definition, await SqlRf3Protocol.SdkAsync<ResourceDefinition>(root.Sdk,
            SqlRf3Protocol.Call(partition, McpCallerTools.ResourcesConfigure,
                new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, definition), Guid.NewGuid()), token));
        var seed = QueryCapabilityVersionRf3Requests.Seed(partition);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(seed, token));
        var reader = await McpPersistedIdentity.CreateAsync(fixture, partition, QueryCapabilityVersionRf3Requests.Collection,
            Capability.Query | Capability.DocumentsRead, token);
        var outbox = await QueryCursorRequestIdentityR47Current.OutboxAsync(root, partition, token);
        await Assert.That(outbox.Head.Tail).IsEqualTo(4L);
        await Assert.That(outbox.Head.FirstAvailable).IsEqualTo(1L);
        await Assert.That(outbox.Head.StoredRecords).IsEqualTo(4L);
        await Assert.That(outbox.Head.StoredBytes).IsGreaterThan(0L);
        await Assert.That(outbox.Consumers).IsEmpty();
        var state = new QueryCapabilityVersionRf3State(seed, receipt, reader, outbox);
        await QueryCapabilityVersionRf3Current.CheckAsync(root, state, token);
        return state;
    }
    internal static async Task PolicyAsync(RequestCqrsRf3Callers root, QueryCapabilityVersionRf3State state, long epoch, bool allow, CancellationToken token)
    {
        var expected = state.Reader.Principal with
        {
            PolicyEpoch = epoch,
            Grants = allow ? [new(state.Partition.DatabaseId, QueryCapabilityVersionRf3Requests.Collection, Capability.Query | Capability.DocumentsRead)] : []
        };
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token)));
    }
}
