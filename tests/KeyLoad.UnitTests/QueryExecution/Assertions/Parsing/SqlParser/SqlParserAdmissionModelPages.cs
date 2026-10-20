using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Authorization;
using KeyLoad.UnitTests.Features.GraphTraversal;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlParserAdmissionModelPages
{
    internal static async Task RunAsync(SqlParserAdmissionFixture fixture, DatabaseEngine database, ZoneTreeStore store, CancellationToken token)
    {
        var partition = fixture.Harness.Partition;
        var source = new StreamRef(partition, SqlParserAdmissionModels.Events, "source");
        await RequiredProcessingPolicyAssertions.EqualAsync(new StreamPage(source, new(2L, 1L, 1L),
            [new(source, 1L, 1L, new("created", "Created", "{\"value\":1}", "{}"), SqlParserAdmissionFixture.Epoch),
             new(source, 2L, 2L, new("updated", "Updated", "{\"value\":2}", "{}"), SqlParserAdmissionFixture.Epoch)],
            database.LastApplied, false, null, database.LastApplied), database.ReadStream("root", new(source), token));
        var engine = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
        var sql = "SELECT TOP (10) payload.value AS value FROM EVENTS('parser-events','source',+1) ORDER BY revision";
        var ast = new AstQueryRequest(partition, new(SqlParserAdmissionModels.Events, null, [new("/payload/value", "value")], null,
            [new("/revision", false)], 10, ModelSource: new(ModelQuerySourceKind.Events, "source", 1L)), AllowFullScan: true);
        var expected = new QueryPage([new("created", 1L, "{\"value\":1}", false, []), new("updated", 2L, "{\"value\":2}", false, [])],
            null, store.Position, "model-scan:events");
        await RequiredProcessingPolicyAssertions.EqualAsync(expected, engine.Execute(SqlOffsetProtocol.Reader, new(partition, sql, AllowFullScan: true), cancellationToken: token));
        await RequiredProcessingPolicyAssertions.EqualAsync(expected, engine.ExecuteAst(SqlOffsetProtocol.Reader, ast, cancellationToken: token));
        var reference = fixture.Harness.ExpectedReference(new(partition, SqlParserAdmissionModels.Table, "typed-row"));
        await RequiredProcessingPolicyAssertions.EqualAsync(new DocumentResult(reference, 1L, SqlParserAdmissionModels.TypedJson, false, []),
            database.GetDocument("root", reference, cancellationToken: token) ?? throw new InvalidOperationException("The literal inserted row is absent."));
        var typed = new QueryPage([new("typed-row", 1L, """{"amount":-1}""", false, [])], null, store.Position, "bounded-full-scan");
        await RequiredProcessingPolicyAssertions.EqualAsync(typed, engine.Execute(SqlOffsetProtocol.Reader,
            new(partition, "SELECT TOP (1) amount FROM [parser-typed] ORDER BY id", AllowFullScan: true), cancellationToken: token));
        await RequiredProcessingPolicyAssertions.EqualAsync(typed, engine.ExecuteAst(SqlOffsetProtocol.Reader,
            new(partition, new(SqlParserAdmissionModels.Table, null, [new("/amount", "amount")], null, [new("/@id", false)], 1), AllowFullScan: true),
            cancellationToken: token));
        foreach (var command in fixture.Additional)
        {
            await GraphLabelUseOperations.ReplayAsync(database, store, command.Request, command.Receipt, command.Outcome);
        }
    }
    internal static Task InsertRefusalAsync(SqlParserAdmissionFixture fixture, DatabaseEngine database, ZoneTreeStore store, CancellationToken token)
        => GraphLabelUseOperations.ObserveAsync(store, async () =>
        {
            McpDecodedOperation? partial = null;
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => partial = SqlParserAdmissionModels.Compile(database, fixture.Harness.Partition,
                Guid.NewGuid(), "INSERT INTO [parser-typed] (id,title,amount) VALUES ('refused','private',-1 COLLATE Latin1_General_100_BIN2)", token));
            await Assert.That(partial).IsNull();
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
            await Assert.That(failure.Message).IsEqualTo("The unified SQL version, statement or target operation is unsupported.");
        });
}
