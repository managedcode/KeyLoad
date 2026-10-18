namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlBetweenSymmetricClusterSetup
{
    internal static async Task<SqlNullOrderingClusterState> CreateAsync(ClusterFixture fixture, CancellationToken token)
    {
        return await global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerProtocol.Node1,
            fixture.AdminKey, async root =>
        {
            var partition = PostgresClusterState.Partition;
            var resource = new ResourceDefinition(SqlNullOrderingClusterProtocol.Collection, ResourceKind.Collection,
                partition.TransactionDomainId);
            var actual = await SqlClusterProtocol.SdkAsync<ResourceDefinition>(root.Sdk,
                SqlClusterProtocol.Call(partition, global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerTools.ResourcesConfigure,
                    new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, resource), Guid.NewGuid()), token);
            await SqlClusterProtocol.EqualAsync(resource, actual);
            var writer = await PostgresClusterSeed.IdentityAsync(root.Sdk, SqlNullOrderingClusterProtocol.WriterMarker,
                Capability.DocumentsRead | Capability.DocumentsWrite | Capability.Query, token, resource.Name);
            var reader = await PostgresClusterSeed.IdentityAsync(root.Sdk, SqlNullOrderingClusterProtocol.ReaderMarker,
                Capability.DocumentsRead | Capability.Query, token, resource.Name);
            var seed = new CommandRequest(Guid.NewGuid(), partition,
                [.. SqlBetweenSymmetricClusterState.Rows.Select(row => new PutDocument(resource.Name, row.Id, row.Json))]);
            var receipt = await global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerProtocol.Node2,
                writer.Secret, async caller =>
            {
                var committed = await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CommitAsync(seed, token));
                await SqlNullOrderingClusterAssertions.ReceiptAsync(seed, committed);
                return committed;
            }, token);
            return new SqlNullOrderingClusterState(writer, reader, seed, receipt);
        }, token);
    }
}
