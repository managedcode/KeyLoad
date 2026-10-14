namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlBooleanOperandControls
{
    internal static async Task InvalidAsync(TestDatabase database, global::KeyLoad.Query.QueryEngine engine,
        string[] image, long position, byte[] identity)
    {
        foreach (var clause in new[] { "score TRUE", "score =", "1", "'true'", "@absent", "NOT" })
        {
            var request = new QueryRequest(database.Partition, SqlBooleanTruthCases.Prefix + clause
                + SqlBooleanTruthCases.Tail, SqlBooleanTruthCases.Parameters(), AllowFullScan: true);
            await global::TUnit.Assertions.Assert.That(global::TUnit.Assertions.Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(
                SqlNullOrderingProtocol.Reader, request)).Code).IsEqualTo(ErrorCode.Validation);
            await SqlNullOrderingRefusals.UnchangedAsync(database.Store, image, position, identity);
            await SqlBooleanTruthPages.HealthyAsync(database.Database, database.Store,
                database.Partition, SqlBooleanTruthRefusals.InitialRevision);
        }
        await CancelledAsync(database, engine, image, position, identity);
        await ParametersAsync(engine, database.Store, database.Partition, SqlBooleanTruthRefusals.InitialRevision);
    }

    internal static async Task FaultAsync(TestDatabase database, global::KeyLoad.Query.QueryEngine engine,
        string[] image, long position, byte[] identity)
    {
        foreach (var item in SqlBooleanTruthCases.All.Where(item => item.Clause == SqlBooleanTruthCases.BareClause
            || item.Clause == SqlBooleanTruthCases.NegatedBareClause))
        {
            await global::TUnit.Assertions.Assert.That(global::TUnit.Assertions.Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(SqlNullOrderingProtocol.Reader,
                new(database.Partition, SqlBooleanTruthCases.Prefix + item.Clause + SqlBooleanTruthCases.Tail, SqlBooleanTruthCases.Parameters(),
                    AllowFullScan: true))).Code).IsEqualTo(ErrorCode.Validation);
            await global::TUnit.Assertions.Assert.That(global::TUnit.Assertions.Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst(SqlNullOrderingProtocol.Reader,
                SqlBooleanTruthCases.Ast(database.Partition, item))).Code).IsEqualTo(ErrorCode.Validation);
            await SqlNullOrderingRefusals.UnchangedAsync(database.Store, image, position, identity);
        }
    }

    private static async Task CancelledAsync(TestDatabase database, global::KeyLoad.Query.QueryEngine engine,
        string[] image, long position, byte[] identity)
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var originalToken = cancelled.Token;
        foreach (var item in SqlBooleanTruthCases.All.Where(item => item.Clause == SqlBooleanTruthCases.BareClause
            || item.Clause == SqlBooleanTruthCases.NegatedBareClause))
        {
            var request = new QueryRequest(database.Partition, SqlBooleanTruthCases.Prefix + item.Clause
                + SqlBooleanTruthCases.Tail, SqlBooleanTruthCases.Parameters(), AllowFullScan: true);
            var error = global::TUnit.Assertions.Assert.ThrowsExactly<OperationCanceledException>(() => engine.Execute(
                SqlNullOrderingProtocol.Reader, request, cancellationToken: originalToken));
            await global::TUnit.Assertions.Assert.That(error.CancellationToken).IsEqualTo(originalToken);
            await SqlNullOrderingRefusals.UnchangedAsync(database.Store, image, position, identity);
            using var healthy = new CancellationTokenSource();
            await global::TUnit.Assertions.Assert.That(healthy.Token.IsCancellationRequested).IsFalse();
            await SqlNullOrderingPages.EqualAsync(Page(item.Ids, position, SqlBooleanTruthRefusals.InitialRevision), engine.Execute(
                SqlNullOrderingProtocol.Reader, request, cancellationToken: healthy.Token));
            await SqlNullOrderingRefusals.UnchangedAsync(database.Store, image, position, identity);
        }
    }

    internal static async Task ParametersAsync(global::KeyLoad.Query.QueryEngine engine,
        global::KeyLoad.Storage.ZoneTree.ZoneTreeStore store, PartitionRef partition, long lowRevision)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(store);
        var position = store.Position;
        var identity = JsonDefaults.Serialize(store.Identity);
        foreach (var value in new bool?[] { true, false, null })
        {
            var parameters = new Dictionary<string, global::System.Text.Json.JsonElement>
            {
                [Flag] = global::System.Text.Json.JsonSerializer.SerializeToElement(value),
                [SqlBooleanTruthCases.TrueParameter] = global::System.Text.Json.JsonSerializer.SerializeToElement(true)
            };
            var item = new SqlBooleanTruthCase(ParameterClause, new global::KeyLoad.Query.Logical(
                new global::KeyLoad.Query.Comparison(new global::KeyLoad.Query.FieldOperand(Score), "=", global::KeyLoad.Query.ValueOperand.Create(true)), "AND",
                new global::KeyLoad.Query.Comparison(new global::KeyLoad.Query.ParameterOperand(Flag), "=", global::KeyLoad.Query.ValueOperand.Create(true))),
                value == true ? [Low] : []);
            var query = new QueryRequest(partition, SqlBooleanTruthCases.Prefix + ParameterClause
                + SqlBooleanTruthCases.Tail, parameters, AllowFullScan: true);
            await SqlNullOrderingPages.EqualAsync(Page(item.Ids, position, lowRevision), engine.Execute(SqlNullOrderingProtocol.Reader, query));
            await SqlNullOrderingPages.EqualAsync(Page(item.Ids, position, lowRevision), engine.ExecuteAst(SqlNullOrderingProtocol.Reader,
                SqlBooleanTruthCases.Ast(partition, item) with
                {
                    Parameters = parameters
                }));
            await SqlNullOrderingRefusals.UnchangedAsync(store, image, position, identity);
        }
    }

    private static global::KeyLoad.QueryPage Page(string[] ids, long position, long lowRevision)
        => new([.. ids.Select(id => new global::KeyLoad.QueryRow(id, id == Low ? lowRevision : SqlBooleanTruthRefusals.InitialRevision,
            "{\"label\":\"" + id + "\"}", RedactedFields: []))], null, position, FullScan);

    private const string Score = "/score";
    private const string Flag = "flag";
    private const string ParameterClause = "score = @truth AND @flag = @truth";
    private const string Low = "low";
    private const string FullScan = "bounded-full-scan";
}
