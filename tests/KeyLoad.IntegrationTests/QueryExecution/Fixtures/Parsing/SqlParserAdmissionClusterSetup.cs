using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlParserAdmissionClusterSetup
{
    private const string IdColumn = "id";
    internal static async Task<SqlParserAdmissionClusterState> RunAsync(ClusterFixture fixture, RequestCqrsClusterCallers root, CancellationToken token)
    {
        var original = await SqlOffsetClusterSetup.RunAsync(fixture, root, token);
        var partition = original.Partition;
        foreach (var definition in new ResourceDefinition[]
        {
            new(SqlParserAdmissionClusterState.EventResource, ResourceKind.StreamSet, partition.TransactionDomainId),
            new(SqlParserAdmissionClusterState.Table, ResourceKind.Collection, partition.TransactionDomainId)
            {
                Authority = DocumentAuthority.Document,
                RelationalSchema = new(IdColumn, [new(IdColumn, RelationalColumnType.Text), new("title", RelationalColumnType.Text), new("amount", RelationalColumnType.WholeNumber)])
            }
        })
        {
            await SqlClusterProtocol.EqualAsync(definition, await SqlClusterProtocol.SdkAsync<ResourceDefinition>(root.Sdk,
                SqlClusterProtocol.Call(partition, McpCallerTools.ResourcesConfigure,
                    new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, definition), Guid.NewGuid()), token));
        }
        var events = new CommandRequest(Guid.NewGuid(), partition,
            [new AppendEvents(SqlParserAdmissionClusterState.EventResource, "source",
                [new("created", "Created", "{\"value\":1}", "{}"), new("updated", "Updated", "{\"value\":2}", "{}")], ExpectedStreamRevision.NoStream)]);
        var eventReceipt = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(events, token));
        var insert = new SqlOperationRequest(partition,
            ScriptDomNamedParameterClusterProtocol.InsertSql, ScriptDomNamedParameterClusterWrites.Parameters(), CommandId: Guid.NewGuid());
        var inserted = await SqlClusterProtocol.SdkAsync<CommitReceipt>(root.Sdk, insert, token);
        var stream = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadStreamAsync(new(new(partition,
            SqlParserAdmissionClusterState.EventResource, "source")), token));
        await SqlParserAdmissionClusterCurrent.StreamAsync(stream, partition);
        var outbox = await QueryCursorRequestIdentityR47Current.OutboxAsync(root, partition, token);
        await Assert.That(outbox.Head.Tail).IsEqualTo(10L);
        await Assert.That(outbox.Head.FirstAvailable).IsEqualTo(1L);
        await Assert.That(outbox.Head.StoredRecords).IsEqualTo(10L);
        await Assert.That(outbox.Head.StoredBytes).IsGreaterThan(0L);
        await Assert.That(outbox.Consumers).IsEmpty();
        var state = new SqlParserAdmissionClusterState(original, events, eventReceipt, insert, inserted,
            stream with
            {
                CutPosition = 0L,
                SnapshotCutPosition = 0L
            }, outbox, fixture.AdminKey);
        await PolicyAsync(root, state, 3L, true, token);
        await SqlParserAdmissionClusterCurrent.CheckAsync(root, state, token);
        return state;
    }
    internal static async Task PolicyAsync(RequestCqrsClusterCallers root, SqlParserAdmissionClusterState state, long epoch, bool allow, CancellationToken token)
    {
        var partition = state.Partition;
        var principal = state.Original.Identity.Principal with
        {
            PolicyEpoch = epoch,
            Grants = allow
            ? [new(partition.DatabaseId, SqlOffsetClusterProtocol.Left, Capability.Query | Capability.DocumentsRead),
               new(partition.DatabaseId, SqlOffsetClusterProtocol.Right, Capability.Query | Capability.DocumentsRead),
               new(partition.DatabaseId, SqlParserAdmissionClusterState.EventResource, Capability.Query | Capability.EventsRead),
               new(partition.DatabaseId, SqlParserAdmissionClusterState.Table, Capability.Query | Capability.DocumentsRead)] : []
        };
        await SqlClusterProtocol.EqualAsync(principal, await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token)));
    }
}
