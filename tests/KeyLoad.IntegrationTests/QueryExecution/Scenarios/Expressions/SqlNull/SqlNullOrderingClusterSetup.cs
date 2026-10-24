using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class SqlNullOrderingClusterSetup
{
    internal static async Task<SqlNullOrderingClusterState> CreateAsync(ClusterFixture fixture, CancellationToken token)
    {
        return await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async root =>
        {
            var partition = PostgresClusterState.Partition;
            var resource = new ResourceDefinition(SqlNullOrderingClusterProtocol.Collection, ResourceKind.Collection,
                partition.TransactionDomainId);
            var creationId = Guid.NewGuid();
            var creation = new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, resource);
            var actual = await SqlClusterProtocol.SdkAsync<ResourceDefinition>(root.Sdk,
                SqlClusterProtocol.Call(partition, McpCallerTools.ResourcesConfigure, creation, creationId), token);
            await SqlClusterProtocol.EqualAsync(resource, actual);
            var writer = await PostgresClusterSeed.IdentityAsync(root.Sdk, SqlNullOrderingClusterProtocol.WriterMarker,
                Capability.DocumentsRead | Capability.DocumentsWrite | Capability.Query, token, resource.Name);
            var reader = await PostgresClusterSeed.IdentityAsync(root.Sdk, SqlNullOrderingClusterProtocol.ReaderMarker,
                Capability.DocumentsRead | Capability.Query, token, resource.Name);
            var seed = new CommandRequest(Guid.NewGuid(), partition,
                [.. SqlNullOrderingClusterState.Rows.Select(row => new PutDocument(resource.Name, row.Id, row.Json))]);
            var receipt = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
                writer.Secret, async caller =>
            {
                var committed = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CommitAsync(seed, token));
                await SqlNullOrderingClusterAssertions.ReceiptAsync(seed, committed);
                return committed;
            }, token);
            return new SqlNullOrderingClusterState(writer, reader, seed, receipt,
                global::KeyLoad.IntegrationTests.ResourceExecution.ResourceCreationLifetime.Create(creationId, creation));
        }, token);
    }
}
