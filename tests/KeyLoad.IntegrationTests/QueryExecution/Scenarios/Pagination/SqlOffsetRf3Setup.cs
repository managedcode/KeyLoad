using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlOffsetRf3Setup
{
    internal static Task<SqlOffsetRf3State> RunAsync(ClusterFixture fixture, RequestCqrsRf3Callers callers,
        CancellationToken token) => RunAsync(fixture, callers, null, token);

    internal static async Task<SqlOffsetRf3State> RunAsync(ClusterFixture fixture, RequestCqrsRf3Callers callers,
        PartitionRef? nativePartition, CancellationToken token)
    {
        var partition = nativePartition ?? new PartitionRef("sql-offset-" + Guid.NewGuid().ToString("N"), "database", "domain", Guid.NewGuid().ToString("N"));
        await ConfigureAsync(callers, partition, new(SqlOffsetRf3Protocol.Left, ResourceKind.Collection, partition.TransactionDomainId)
        {
            RelationalSchema = new(SqlOffsetRf3Protocol.Order, [new(SqlOffsetRf3Protocol.Order, RelationalColumnType.Text),
            new(SqlOffsetRf3Protocol.Foreign, RelationalColumnType.Text), new(SqlOffsetRf3Protocol.Title, RelationalColumnType.Text)])
        }, token);
        await ConfigureAsync(callers, partition, new(SqlOffsetRf3Protocol.Right, ResourceKind.Collection, partition.TransactionDomainId)
        {
            RelationalSchema = new(SqlOffsetRf3Protocol.Id, [new(SqlOffsetRf3Protocol.Id, RelationalColumnType.Text),
            new(SqlOffsetRf3Protocol.Name, RelationalColumnType.Text)])
        }, token);
        var command = new CommandRequest(Guid.NewGuid(), partition,
        [
            new PutDocument(SqlOffsetRf3Protocol.Left, "a", """{"order_id":"a","customer_id":"c1","title":"first"}"""),
            new PutDocument(SqlOffsetRf3Protocol.Left, "b", """{"order_id":"b","customer_id":"c2","title":"second"}"""),
            new PutDocument(SqlOffsetRf3Protocol.Left, "c", """{"order_id":"c","customer_id":"c3","title":"third"}"""),
            new PutDocument(SqlOffsetRf3Protocol.Left, "d", """{"order_id":"d","customer_id":"c4","title":"fourth"}"""),
            new PutDocument(SqlOffsetRf3Protocol.Right, "c1", """{"id":"c1","name":"Aye"}"""),
            new PutDocument(SqlOffsetRf3Protocol.Right, "c2", """{"id":"c2","name":"Bee"}"""),
            new PutDocument(SqlOffsetRf3Protocol.Right, "c3", """{"id":"c3","name":"See"}"""),
            new PutDocument(SqlOffsetRf3Protocol.Right, "c4", """{"id":"c4","name":"Dee"}""")
        ]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(command, token));
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, partition, SqlOffsetRf3Protocol.Left,
            Capability.Query | Capability.DocumentsRead, token);
        var state = new SqlOffsetRf3State(partition, command, receipt, identity);
        await PolicyAsync(callers, state, epoch: 2, allow: true, token);
        return state;
    }
    private static async Task ConfigureAsync(RequestCqrsRf3Callers callers, PartitionRef partition,
        ResourceDefinition definition, CancellationToken token)
    {
        var actual = await SqlRf3Protocol.SdkAsync<ResourceDefinition>(callers.Sdk, SqlRf3Protocol.Call(partition,
            McpCallerTools.ResourcesConfigure, new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, definition), Guid.NewGuid()), token);
        await SqlRf3Protocol.EqualAsync(definition, actual);
    }
    internal static async Task PolicyAsync(RequestCqrsRf3Callers callers, SqlOffsetRf3State state,
        long epoch, bool allow, CancellationToken token)
    {
        var expected = state.Identity.Principal with
        {
            PolicyEpoch = epoch,
            Grants = allow
            ? [new(state.Partition.DatabaseId, SqlOffsetRf3Protocol.Left, Capability.Query | Capability.DocumentsRead),
               new(state.Partition.DatabaseId, SqlOffsetRf3Protocol.Right, Capability.Query | Capability.DocumentsRead)] : []
        };
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await callers.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token)));
    }
}
