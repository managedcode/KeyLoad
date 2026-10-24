using KeyLoad.Query;

namespace KeyLoad.UnitTests.QueryExecution;

internal static class SqlNullDistinctRefusals
{
    internal static async Task InvalidAsync(TestDatabase database)
    {
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var before = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var position = database.Store.Position;
        var identity = JsonDefaults.Serialize(database.Store.Identity);
        await SqlStringContinuationControls.InvalidAsync(database, engine);
        await SqlLiteralFormsControls.InvalidAsync(database, engine);
        foreach (var clause in new[] { "score IS DISTINCT NULL", "score IS DISTINCT FROM", "score IS 'DISTINCT' FROM NULL",
            "score IS NOT DISTINCT FROM @absent", "score IS ISNULL", "score IS NOT NOTNULL",
            "score 'ISNULL'", "score NOT ISNULL", "score = +",
             "score = +79228162514264337593543950336",
            "score = 0x1.0", "score = 0x1_0.0", "score = 0b1.0", "score = 0o1.0",
            "score = .e1", "score = ..5", "score = .5e+", "score = .5.0", "score = +.x", "score = -.x",
            "score = 0xG",
            "label = E'\\u123'", "label = E'\\U00110000'", "label = E'\\uD800'",
            "label = E'\\uDC00'", "label = E'\\000'", "label = E'\\x00'", "label = E'\\xff'",
            "label = E'\\xc3'", "label = E'unterminated", "label = E'\\'" })
        {
            var actual = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(SqlNullOrderingProtocol.Reader,
                new(database.Partition, SqlNullDistinctCases.Prefix + clause + SqlNullDistinctCases.Tail, AllowFullScan: true)));
            await Assert.That(actual.Code).IsEqualTo(ErrorCode.Validation);
            await SqlNullOrderingRefusals.UnchangedAsync(database.Store, before, position, identity);
            await SqlNullDistinctPages.HealthyAsync(database.Database, database.Store, database.Partition, InitialRevision);
            await SqlNullOrderingRefusals.UnchangedAsync(database.Store, before, position, identity);
        }
        foreach (var window in new[]
        {
            "OFFSET -1 ROWS FETCH NEXT 1 ROWS ONLY",
            "OFFSET 0 ROWS FETCH NEXT +0 ROWS ONLY",
            "OFFSET 0 ROWS FETCH NEXT -1 ROWS ONLY",
            "OFFSET .5 ROWS FETCH NEXT 1 ROWS ONLY",
            "OFFSET 0 ROWS FETCH NEXT .5 ROWS ONLY",
            "OFFSET 0 ROWS FETCH NEXT 1e0 ROWS ONLY",
            "OFFSET 0x1 ROWS FETCH NEXT 1 ROWS ONLY",
            "OFFSET 0 ROWS FETCH NEXT 0x1 ROWS ONLY",
            "OFFSET 0 ROWS FETCH NEXT 0x0 ROWS ONLY"
        })
        {
            var request = new QueryRequest(database.Partition, SqlNullDistinctCases.WindowPrefix
                + SqlNullDistinctCases.PositiveClause + SqlNullDistinctCases.Tail + " " + window, AllowFullScan: true);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(SqlNullOrderingProtocol.Reader,
                request)).Code).IsEqualTo(ErrorCode.Validation);
            await SqlNullOrderingRefusals.UnchangedAsync(database.Store, before, position, identity);
            await SqlNullDistinctPages.HealthyAsync(database.Database, database.Store, database.Partition, InitialRevision);
            await SqlNullOrderingRefusals.UnchangedAsync(database.Store, before, position, identity);
        }
        foreach (var clause in new[] { "score = 0x", "score = 0x1000000000000000000000000", "score = -0x1000000000000000000000000",
            "score = +true", "score = +@absent", "score = ++1" })
        {
            var request = new QueryRequest(database.Partition, SqlNullDistinctCases.Prefix + clause
                + SqlNullDistinctCases.Tail, AllowFullScan: true);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(SqlNullOrderingProtocol.Reader,
                request)).Code).IsEqualTo(ErrorCode.UnsupportedCapability);
            await SqlNullOrderingRefusals.UnchangedAsync(database.Store, before, position, identity);
            await SqlNullDistinctPages.HealthyAsync(database.Database, database.Store, database.Partition, InitialRevision);
            await SqlNullOrderingRefusals.UnchangedAsync(database.Store, before, position, identity);
        }
        await HealthyAndCancelledAsync(database, engine, before, position, identity);
    }

    private static async Task HealthyAndCancelledAsync(TestDatabase database, QueryEngine engine,
        string[] before, long position, byte[] identity)
    {
        var spacedPositive = new global::KeyLoad.QueryPage([new(Low, InitialRevision, LowProjection, RedactedFields: [])],
            null, position, FullScan);
        await SqlNullOrderingPages.EqualAsync(spacedPositive, engine.Execute(SqlNullOrderingProtocol.Reader,
            new(database.Partition, SqlNullDistinctCases.Prefix + "score = + 1" + SqlNullDistinctCases.Tail, AllowFullScan: true)));
        await SqlNullOrderingRefusals.UnchangedAsync(database.Store, before, position, identity);
        await SqlNullOrderingPages.EqualAsync(spacedPositive, engine.Execute(SqlNullOrderingProtocol.Reader,
            new(database.Partition, SqlNullDistinctCases.WindowPrefix + SqlNullDistinctCases.PositiveClause
                + SqlNullDistinctCases.Tail + " OFFSET +0 ROWS FETCH NEXT +1 ROWS ONLY", AllowFullScan: true)));
        await SqlNullOrderingRefusals.UnchangedAsync(database.Store, before, position, identity);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        foreach (var clause in new[] { SqlNullDistinctCases.All[First].Clause, SqlNullDistinctCases.NullClause,
            SqlNullDistinctCases.NotNullClause, SqlNullDistinctCases.PositiveClause, SqlNullDistinctCases.LeadingClause,
            SqlNullDistinctCases.HexClause,
            SqlNullDistinctCases.EscapeClause, SqlNullDistinctCases.ContinuationClause })
        {
            var request = new QueryRequest(database.Partition, SqlNullDistinctCases.Prefix + clause
                + SqlNullDistinctCases.Tail, AllowFullScan: true);
            var error = Assert.ThrowsExactly<OperationCanceledException>(() => engine.Execute(SqlNullOrderingProtocol.Reader,
                request, cancellationToken: cancelled.Token));
            await Assert.That(error.CancellationToken).IsEqualTo(cancelled.Token);
            await SqlNullOrderingRefusals.UnchangedAsync(database.Store, before, position, identity);
            await SqlNullDistinctPages.HealthyAsync(database.Database, database.Store, database.Partition, InitialRevision);
            await SqlNullOrderingRefusals.UnchangedAsync(database.Store, before, position, identity);
        }
    }

    internal static async Task TypeRepairAsync(TestDatabase database)
    {
        var faultCommand = new CommandRequest(Guid.NewGuid(), database.Partition,
            [new PutDocument(SqlNullOrderingProtocol.Collection, Low, FaultJson)]);
        var fault = database.Submit(OperationKind.Batch, faultCommand, id: faultCommand.CommandId).Get<CommitReceipt>();
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var before = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var position = database.Store.Position;
        var identity = JsonDefaults.Serialize(database.Store.Identity);
        foreach (var item in new[] { SqlNullDistinctCases.All[AgainstOne],
            SqlNullDistinctCases.All.Single(value => value.Clause == SqlNullDistinctCases.PositiveClause),
            SqlNullDistinctCases.All.Single(value => value.Clause == SqlNullDistinctCases.LeadingClause),
            SqlNullDistinctCases.All.Single(value => value.Clause == SqlNullDistinctCases.HexClause),
            SqlNullDistinctCases.All.Single(value => value.Clause == SqlNullDistinctCases.EscapeClause),
            SqlNullDistinctCases.All.Single(value => value.Clause == SqlNullDistinctCases.ContinuationClause),
            SqlNullDistinctCases.All.Single(value => value.Clause == SqlNullDistinctCases.UnicodeClause),
            SqlNullDistinctCases.All.Single(value => value.Clause == SqlNullDistinctCases.UnicodeCustomClause),
            SqlNullDistinctCases.All.Single(value => value.Clause == SqlUnicodeIdentifierCases.Clause),
            SqlNullDistinctCases.All.Single(value => value.Clause == SqlUnicodeIdentifierCases.WideClause),
            SqlNullDistinctCases.All.Single(value => value.Clause == SqlUnicodeIdentifierCases.CustomClause),
            SqlNullDistinctCases.All.Single(value => value.Clause == SqlNullDistinctCases.DollarClause),
            SqlNullDistinctCases.All.Single(value => value.Clause == SqlNullDistinctCases.BinaryClause),
            SqlNullDistinctCases.All.Single(value => value.Clause == SqlNullDistinctCases.OctalClause),
            SqlNullDistinctCases.All.Single(value => value.Clause == SqlNullDistinctCases.NumericSeparatorClause),
            SqlNullDistinctCases.All.Single(value => value.Clause == SqlNullDistinctCases.NumericRadixSeparatorClause),
            SqlNullDistinctCases.All.Single(value => value.Clause == SqlNullDistinctCases.NumericOctalSeparatorClause) })
        {
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(SqlNullOrderingProtocol.Reader,
                new(database.Partition, SqlNullDistinctCases.Prefix + item.Clause + SqlNullDistinctCases.Tail,
                    AllowFullScan: true))).Code).IsEqualTo(ErrorCode.Validation);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst(SqlNullOrderingProtocol.Reader,
                SqlNullDistinctCases.Ast(database.Partition, item))).Code).IsEqualTo(ErrorCode.Validation);
            await SqlNullOrderingRefusals.UnchangedAsync(database.Store, before, position, identity);
        }
        var repairCommand = new CommandRequest(Guid.NewGuid(), database.Partition,
            [new PutDocument(SqlNullOrderingProtocol.Collection, Low, HealthyJson)]);
        var repaired = database.Submit(OperationKind.Batch, repairCommand, id: repairCommand.CommandId).Get<CommitReceipt>();
        await Assert.That(repaired.CommandId).IsNotEqualTo(fault.CommandId);
        await Assert.That(repaired.Mutations).IsEquivalentTo([new MutationReceipt(PutKind,
            SqlNullOrderingProtocol.Collection, Low, RepairedRevision)], global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
        var healthy = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var cut = database.Store.Position;
        await SqlNullOrderingPages.ReplayAsync(database.Database, faultCommand, fault);
        await SqlNullOrderingPages.ReplayAsync(database.Database, repairCommand, repaired);
        await SqlNullOrderingRefusals.UnchangedAsync(database.Store, healthy, cut, identity);
    }

    private const int First = 0;
    private const int AgainstOne = 2;
    internal const long InitialRevision = 1;
    internal const long RepairedRevision = 3;
    private const string Low = "low";
    private const string LowProjection = "{\"label\":\"low\"}";
    private const string FullScan = "bounded-full-scan";
    private const string PutKind = "putDocument";
    private const string FaultJson = "{\"label\":\"low\",\"score\":true}";
    private const string HealthyJson = "{\"label\":\"low\",\"score\":1}";
}
