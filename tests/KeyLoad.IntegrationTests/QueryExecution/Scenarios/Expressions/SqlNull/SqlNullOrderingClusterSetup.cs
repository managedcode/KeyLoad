using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

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
            var actual = await SqlClusterProtocol.SdkAsync<ResourceDefinition>(root.Sdk,
                SqlClusterProtocol.Call(partition, McpCallerTools.ResourcesConfigure,
                    new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, resource), Guid.NewGuid()), token);
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
            return new SqlNullOrderingClusterState(writer, reader, seed, receipt);
        }, token);
    }
}
