namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlBetweenSymmetricRf3Setup
{
    internal static async Task<SqlNullOrderingRf3State> CreateAsync(ClusterFixture fixture, CancellationToken token)
    {
        return await global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerProtocol.Node1,
            fixture.AdminKey, async root =>
        {
            var partition = PostgresRf3State.Partition;
            var resource = new ResourceDefinition(SqlNullOrderingRf3Protocol.Collection, ResourceKind.Collection,
                partition.TransactionDomainId);
            var actual = await SqlRf3Protocol.SdkAsync<ResourceDefinition>(root.Sdk,
                SqlRf3Protocol.Call(partition, global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerTools.ResourcesConfigure,
                    new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, resource), Guid.NewGuid()), token);
            await SqlRf3Protocol.EqualAsync(resource, actual);
            var writer = await PostgresRf3Seed.IdentityAsync(root.Sdk, SqlNullOrderingRf3Protocol.WriterMarker,
                Capability.DocumentsRead | Capability.DocumentsWrite | Capability.Query, token, resource.Name);
            var reader = await PostgresRf3Seed.IdentityAsync(root.Sdk, SqlNullOrderingRf3Protocol.ReaderMarker,
                Capability.DocumentsRead | Capability.Query, token, resource.Name);
            var seed = new CommandRequest(Guid.NewGuid(), partition,
                [.. SqlBetweenSymmetricRf3State.Rows.Select(row => new PutDocument(resource.Name, row.Id, row.Json))]);
            var receipt = await global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerProtocol.Node2,
                writer.Secret, async caller =>
            {
                var committed = await global::KeyLoad.IntegrationTests.Features.ClientApi.McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CommitAsync(seed, token));
                await SqlNullOrderingRf3Assertions.ReceiptAsync(seed, committed);
                return committed;
            }, token);
            return new SqlNullOrderingRf3State(writer, reader, seed, receipt);
        }, token);
    }
}
