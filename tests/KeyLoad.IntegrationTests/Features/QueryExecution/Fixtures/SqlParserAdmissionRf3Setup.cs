using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlParserAdmissionRf3Setup
{
    private const string IdColumn = "id";
    internal static async Task<SqlParserAdmissionRf3State> RunAsync(ClusterFixture fixture, RequestCqrsRf3Callers root, CancellationToken token)
    {
        var original = await SqlOffsetRf3Setup.RunAsync(fixture, root, token);
        var partition = original.Partition;
        foreach (var definition in new ResourceDefinition[]
        {
            new(SqlParserAdmissionRf3State.EventResource, ResourceKind.StreamSet, partition.TransactionDomainId),
            new(SqlParserAdmissionRf3State.Table, ResourceKind.Collection, partition.TransactionDomainId)
            {
                Authority = DocumentAuthority.Document,
                RelationalSchema = new(IdColumn, [new(IdColumn, RelationalColumnType.Text), new("title", RelationalColumnType.Text), new("amount", RelationalColumnType.WholeNumber)])
            }
        })
        {
            await SqlRf3Protocol.EqualAsync(definition, await SqlRf3Protocol.SdkAsync<ResourceDefinition>(root.Sdk,
                SqlRf3Protocol.Call(partition, McpCallerTools.ResourcesConfigure,
                    new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, definition), Guid.NewGuid()), token));
        }
        var events = new CommandRequest(Guid.NewGuid(), partition,
            [new AppendEvents(SqlParserAdmissionRf3State.EventResource, "source",
                [new("created", "Created", "{\"value\":1}", "{}"), new("updated", "Updated", "{\"value\":2}", "{}")], ExpectedStreamRevision.NoStream)]);
        var eventReceipt = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(events, token));
        var insert = new SqlOperationRequest(partition,
            "INSERT INTO [parser-typed] (id,title,amount) VALUES ('typed-row','literal',-1)", CommandId: Guid.NewGuid());
        var inserted = await SqlRf3Protocol.SdkAsync<CommitReceipt>(root.Sdk, insert, token);
        var stream = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadStreamAsync(new(new(partition,
            SqlParserAdmissionRf3State.EventResource, "source")), token));
        await SqlParserAdmissionRf3Current.StreamAsync(stream, partition);
        var outbox = await QueryCursorRequestIdentityR47Current.OutboxAsync(root, partition, token);
        await Assert.That(outbox.Head.Tail).IsEqualTo(10L);
        await Assert.That(outbox.Head.FirstAvailable).IsEqualTo(1L);
        await Assert.That(outbox.Head.StoredRecords).IsEqualTo(10L);
        await Assert.That(outbox.Head.StoredBytes).IsGreaterThan(0L);
        await Assert.That(outbox.Consumers).IsEmpty();
        var state = new SqlParserAdmissionRf3State(original, events, eventReceipt, insert, inserted,
            stream with
            {
                CutPosition = 0L,
                SnapshotCutPosition = 0L
            }, outbox, fixture.AdminKey);
        await PolicyAsync(root, state, 3L, true, token);
        await SqlParserAdmissionRf3Current.CheckAsync(root, state, token);
        return state;
    }
    internal static async Task PolicyAsync(RequestCqrsRf3Callers root, SqlParserAdmissionRf3State state, long epoch, bool allow, CancellationToken token)
    {
        var partition = state.Partition;
        var principal = state.Original.Identity.Principal with
        {
            PolicyEpoch = epoch,
            Grants = allow
            ? [new(partition.DatabaseId, SqlOffsetRf3Protocol.Left, Capability.Query | Capability.DocumentsRead),
               new(partition.DatabaseId, SqlOffsetRf3Protocol.Right, Capability.Query | Capability.DocumentsRead),
               new(partition.DatabaseId, SqlParserAdmissionRf3State.EventResource, Capability.Query | Capability.EventsRead),
               new(partition.DatabaseId, SqlParserAdmissionRf3State.Table, Capability.Query | Capability.DocumentsRead)] : []
        };
        await SqlRf3Protocol.EqualAsync(principal, await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token)));
    }
}
