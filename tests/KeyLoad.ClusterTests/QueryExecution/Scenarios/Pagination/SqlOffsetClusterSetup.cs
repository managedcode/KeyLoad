using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.ResourceExecution;

namespace KeyLoad.ClusterTests.QueryExecution;

internal static class SqlOffsetClusterSetup
{
    internal static Task<SqlOffsetClusterState> RunAsync(ClusterFixture fixture, RequestCqrsClusterCallers callers,
        CancellationToken token) => RunAsync(fixture, callers, null, token);

    internal static async Task<SqlOffsetClusterState> RunAsync(ClusterFixture fixture, RequestCqrsClusterCallers callers,
        PartitionRef? runtimePartition, CancellationToken token)
    {
        var partition = runtimePartition ?? new PartitionRef("sql-offset-" + Guid.NewGuid().ToString("N"), "database", "domain", Guid.NewGuid().ToString("N"));
        var leftLifetime = await ConfigureAsync(callers, partition, new(SqlOffsetClusterProtocol.Left, ResourceKind.Collection, partition.TransactionDomainId)
        {
            RelationalSchema = new(SqlOffsetClusterProtocol.Order, [new(SqlOffsetClusterProtocol.Order, RelationalColumnType.Text),
            new(SqlOffsetClusterProtocol.Foreign, RelationalColumnType.Text), new(SqlOffsetClusterProtocol.Title, RelationalColumnType.Text)])
        }, token);
        var rightLifetime = await ConfigureAsync(callers, partition, new(SqlOffsetClusterProtocol.Right, ResourceKind.Collection, partition.TransactionDomainId)
        {
            RelationalSchema = new(SqlOffsetClusterProtocol.Id, [new(SqlOffsetClusterProtocol.Id, RelationalColumnType.Text),
            new(SqlOffsetClusterProtocol.Name, RelationalColumnType.Text)])
        }, token);
        var command = new CommandRequest(Guid.NewGuid(), partition,
        [
            new PutDocument(SqlOffsetClusterProtocol.Left, "a", """{"order_id":"a","customer_id":"c1","title":"first"}"""),
            new PutDocument(SqlOffsetClusterProtocol.Left, "b", """{"order_id":"b","customer_id":"c2","title":"second"}"""),
            new PutDocument(SqlOffsetClusterProtocol.Left, "c", """{"order_id":"c","customer_id":"c3","title":"third"}"""),
            new PutDocument(SqlOffsetClusterProtocol.Left, "d", """{"order_id":"d","customer_id":"c4","title":"fourth"}"""),
            new PutDocument(SqlOffsetClusterProtocol.Right, "c1", """{"id":"c1","name":"Aye"}"""),
            new PutDocument(SqlOffsetClusterProtocol.Right, "c2", """{"id":"c2","name":"Bee"}"""),
            new PutDocument(SqlOffsetClusterProtocol.Right, "c3", """{"id":"c3","name":"See"}"""),
            new PutDocument(SqlOffsetClusterProtocol.Right, "c4", """{"id":"c4","name":"Dee"}""")
        ]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(command, token));
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(receipt.PersistedCopies).IsEqualTo(3);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, partition, SqlOffsetClusterProtocol.Left,
            Capability.Query | Capability.DocumentsRead, token);
        var state = new SqlOffsetClusterState(partition, command, receipt, identity, leftLifetime, rightLifetime);
        await PolicyAsync(callers, state, epoch: 2, allow: true, token);
        return state;
    }
    private static async Task<EntityRefLifetime> ConfigureAsync(RequestCqrsClusterCallers callers, PartitionRef partition,
        ResourceDefinition definition, CancellationToken token)
    {
        var creationId = Guid.NewGuid();
        var creation = new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, definition);
        var actual = await SqlClusterProtocol.SdkAsync<ResourceDefinition>(callers.Sdk, SqlClusterProtocol.Call(partition,
            McpCallerTools.ResourcesConfigure, creation, creationId), token);
        await SqlClusterProtocol.EqualAsync(definition, actual);
        return ResourceCreationLifetime.Create(creationId, creation);
    }
    internal static async Task PolicyAsync(RequestCqrsClusterCallers callers, SqlOffsetClusterState state,
        long epoch, bool allow, CancellationToken token)
    {
        var expected = state.Identity.Principal with
        {
            PolicyEpoch = epoch,
            Grants = allow
            ? [new(state.Partition.DatabaseId, SqlOffsetClusterProtocol.Left, Capability.Query | Capability.DocumentsRead),
               new(state.Partition.DatabaseId, SqlOffsetClusterProtocol.Right, Capability.Query | Capability.DocumentsRead)] : []
        };
        await SqlClusterProtocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await callers.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token)));
    }
}
