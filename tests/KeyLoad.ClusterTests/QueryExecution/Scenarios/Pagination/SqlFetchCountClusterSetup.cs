using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.ResourceExecution;
namespace KeyLoad.ClusterTests.QueryExecution;

internal static class SqlFetchCountClusterSetup
{
    internal static async Task<SqlFetchCountClusterState> CreateAsync(ClusterFixture fixture, CancellationToken token)
    {
        return await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async root =>
        {
            var partition = PostgresClusterState.Partition;
            var resource = new ResourceDefinition(SqlFetchCountClusterProtocol.Collection, ResourceKind.Collection,
                partition.TransactionDomainId);
            var creationId = Guid.NewGuid();
            var creation = new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, resource);
            var actual = await SqlClusterProtocol.SdkAsync<ResourceDefinition>(root.Sdk,
                SqlClusterProtocol.Call(partition, McpCallerTools.ResourcesConfigure, creation, creationId), token);
            await SqlClusterProtocol.EqualAsync(resource, actual);
            var writer = await PostgresClusterSeed.IdentityAsync(root.Sdk, SqlFetchCountClusterProtocol.WriterMarker,
                Capability.DocumentsRead | Capability.DocumentsWrite | Capability.Query, token, resource.Name);
            var reader = await PostgresClusterSeed.IdentityAsync(root.Sdk, SqlFetchCountClusterProtocol.ReaderMarker,
                Capability.DocumentsRead | Capability.Query, token, resource.Name);
            var seed = new CommandRequest(Guid.NewGuid(), partition,
                [.. SqlFetchCountClusterState.Rows.Select(row => new PutDocument(resource.Name, row.Id, row.Json))]);
            var receipt = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
                writer.Secret, async caller =>
            {
                var committed = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CommitAsync(seed, token));
                await SqlFetchCountClusterAssertions.ReceiptAsync(seed, committed);
                return committed;
            }, token);
            return new SqlFetchCountClusterState(writer, reader, seed, receipt, ResourceCreationLifetime.Create(creationId, creation));
        }, token);
    }
}
