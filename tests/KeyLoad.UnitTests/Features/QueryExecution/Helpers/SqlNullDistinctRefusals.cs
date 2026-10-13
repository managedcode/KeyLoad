using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlNullDistinctRefusals
{
    internal static async Task InvalidAsync(TestDatabase database)
    {
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var before = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var position = database.Store.Position;
        var identity = JsonDefaults.Serialize(database.Store.Identity);
        foreach (var clause in new[] { "score IS DISTINCT NULL", "score IS DISTINCT FROM", "score IS 'DISTINCT' FROM NULL",
            "score IS NOT DISTINCT FROM @absent" })
        {
            var actual = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(SqlNullOrderingProtocol.Reader,
                new(database.Partition, SqlNullDistinctCases.Prefix + clause + SqlNullDistinctCases.Tail, AllowFullScan: true)));
            await Assert.That(actual.Code).IsEqualTo(ErrorCode.Validation);
            await SqlNullOrderingRefusals.UnchangedAsync(database.Store, before, position, identity);
        }
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var request = new QueryRequest(database.Partition, SqlNullDistinctCases.Prefix
            + SqlNullDistinctCases.All[First].Clause + SqlNullDistinctCases.Tail, AllowFullScan: true);
        var error = Assert.ThrowsExactly<OperationCanceledException>(() => engine.Execute(SqlNullOrderingProtocol.Reader,
            request, cancellationToken: cancelled.Token));
        await Assert.That(error.CancellationToken).IsEqualTo(cancelled.Token);
        await SqlNullOrderingRefusals.UnchangedAsync(database.Store, before, position, identity);
    }

    internal static async Task TypeRepairAsync(TestDatabase database)
    {
        var fault = database.Commit(new PutDocument(SqlNullOrderingProtocol.Collection, Low, FaultJson));
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var before = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var position = database.Store.Position;
        var identity = JsonDefaults.Serialize(database.Store.Identity);
        var item = SqlNullDistinctCases.All[AgainstOne];
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(SqlNullOrderingProtocol.Reader,
            new(database.Partition, SqlNullDistinctCases.Prefix + item.Clause + SqlNullDistinctCases.Tail,
                AllowFullScan: true))).Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst(SqlNullOrderingProtocol.Reader,
            SqlNullDistinctCases.Ast(database.Partition, item))).Code).IsEqualTo(ErrorCode.Validation);
        await SqlNullOrderingRefusals.UnchangedAsync(database.Store, before, position, identity);
        var repaired = database.Commit(new PutDocument(SqlNullOrderingProtocol.Collection, Low, HealthyJson));
        await Assert.That(repaired.CommandId).IsNotEqualTo(fault.CommandId);
        await Assert.That(repaired.Mutations).IsEquivalentTo([new MutationReceipt(PutKind,
            SqlNullOrderingProtocol.Collection, Low, RepairedRevision)], global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    private const int First = 0;
    private const int AgainstOne = 2;
    internal const long InitialRevision = 1;
    internal const long RepairedRevision = 3;
    private const string Low = "low";
    private const string PutKind = "putDocument";
    private const string FaultJson = "{\"label\":\"low\",\"score\":true}";
    private const string HealthyJson = "{\"label\":\"low\",\"score\":1}";
}
