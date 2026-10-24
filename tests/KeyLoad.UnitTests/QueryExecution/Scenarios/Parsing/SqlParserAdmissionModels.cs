using KeyLoad.Server;
using KeyLoad.UnitTests.Authorization;

namespace KeyLoad.UnitTests.QueryExecution;

internal static class SqlParserAdmissionModels
{
    private const string IdColumn = "id";
    internal const string Events = "parser-events";
    internal const string Table = "parser-typed";
    internal const string Insert = "INSERT INTO [parser-typed] (id,title,amount) VALUES ('typed-row','literal',-1)";
    internal const string TypedJson = """{"id":"typed-row","title":"literal","amount":-1}""";
    internal static async Task ConfigureAsync(SqlParserAdmissionFixture fixture)
    {
        var db = fixture.Harness;
        foreach (var definition in new ResourceDefinition[]
        {
            new(Events, ResourceKind.StreamSet, db.Partition.TransactionDomainId),
            new(Table, ResourceKind.Collection, db.Partition.TransactionDomainId)
            {
                Authority = DocumentAuthority.Document,
                RelationalSchema = new(IdColumn, [new(IdColumn, RelationalColumnType.Text), new("title", RelationalColumnType.Text),
                    new("amount", RelationalColumnType.WholeNumber)])
            }
        })
        {
            await RequiredProcessingPolicyAssertions.EqualAsync(definition, db.Submit(OperationKind.ConfigureResource,
                new ConfigureResourceRequest(db.Partition.TenantId, db.Partition.DatabaseId, definition)).Get<ResourceDefinition>());
        }
        var append = new CommandRequest(Guid.NewGuid(), db.Partition,
            [new AppendEvents(Events, "source", [new("created", "Created", "{\"value\":1}", "{}"),
                new("updated", "Updated", "{\"value\":2}", "{}")], ExpectedStreamRevision.NoStream)]);
        await RememberAsync(fixture, append, [new("appendEvents", Events, "source", 2L)]);
        var id = Guid.NewGuid();
        var compiled = Compile(db.Database, db.Partition, id, Insert, CancellationToken.None);
        var request = OrleansSerialization.Deserialize<CommandRequest>(compiled.Payload.Span);
        await RequiredProcessingPolicyAssertions.EqualAsync(new CommandRequest(id, db.Partition, [new InsertTypedRow(Table, TypedJson)]), request);
        await RememberAsync(fixture, request, [new("putDocument", Table, "typed-row", 1L)]);
    }
    private static async Task RememberAsync(SqlParserAdmissionFixture fixture, CommandRequest request, MutationReceipt[] mutations)
    {
        var db = fixture.Harness;
        var next = db.ReadCanonicalCut().AppliedPosition + 1L;
        var expected = new CommitReceipt(request.CommandId, new(db.Store.Identity.Incarnation, db.Partition.AtomicPartitionId, next), [.. mutations], DurabilityProfile.ProcessDurable, 1);
        await RequiredProcessingPolicyAssertions.ReceiptAsync(expected, db.Submit(OperationKind.Batch, request, id: request.CommandId).Get<CommitReceipt>());
        fixture.Additional.Add((request, expected, RequiredProcessingPolicyAssertions.Outcome(db.Store, db.Partition, "root", request.CommandId)));
    }
    internal static McpDecodedOperation Compile(DatabaseEngine database, PartitionRef partition, Guid id, string sql, CancellationToken token)
        => SqlOperationCompiler.Compile(new(partition, sql, CommandId: id), database.OperationLimitsOptions,
            database.Limits.MaxBatchBytes, UnitExecutionOptions.QueryExecution(), token);
}
