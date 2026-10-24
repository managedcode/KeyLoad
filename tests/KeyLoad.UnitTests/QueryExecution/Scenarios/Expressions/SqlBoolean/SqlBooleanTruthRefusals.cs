using KeyLoad.Query;

namespace KeyLoad.UnitTests.QueryExecution;

internal static class SqlBooleanTruthRefusals
{
    internal static async Task InvalidAsync(TestDatabase database)
    {
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var before = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var position = database.Store.Position;
        var identity = JsonDefaults.Serialize(database.Store.Identity);
        await SqlBooleanOperandControls.InvalidAsync(database, engine, before, position, identity);
        foreach (var clause in new[] { "score IS", "score IS NOT", "score IS 'TRUE'",
            "score IS @absent", "score IS TRUE", "score IS FALSE", "score IS UNKNOWN" })
        {
            var actual = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(SqlNullOrderingProtocol.Reader,
                new(database.Partition, SqlBooleanTruthCases.Prefix + clause + SqlBooleanTruthCases.Tail, SqlBooleanTruthCases.Parameters(), AllowFullScan: true)));
            await Assert.That(actual.Code).IsEqualTo(ErrorCode.Validation);
            await SqlNullOrderingRefusals.UnchangedAsync(database.Store, before, position, identity);
        }
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var request = new QueryRequest(database.Partition, SqlBooleanTruthCases.Prefix
            + SqlBooleanTruthCases.All[First].Clause + SqlBooleanTruthCases.Tail, SqlBooleanTruthCases.Parameters(), AllowFullScan: true);
        var error = Assert.ThrowsExactly<OperationCanceledException>(() => engine.Execute(SqlNullOrderingProtocol.Reader,
            request, cancellationToken: cancelled.Token));
        await Assert.That(error.CancellationToken).IsEqualTo(cancelled.Token);
        await SqlNullOrderingRefusals.UnchangedAsync(database.Store, before, position, identity);
    }

    internal static async Task TypeRepairAsync(TestDatabase database)
    {
        var faultCommand = new CommandRequest(Guid.NewGuid(), database.Partition,
            [new PutDocument(SqlNullOrderingProtocol.Collection, Low, FaultJson, InitialRevision)]);
        var fault = database.Submit(OperationKind.Batch, faultCommand, id: faultCommand.CommandId).Get<CommitReceipt>();
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var before = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var position = database.Store.Position;
        var identity = JsonDefaults.Serialize(database.Store.Identity);
        var item = SqlBooleanTruthCases.All[AgainstOne];
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(SqlNullOrderingProtocol.Reader,
            new(database.Partition, SqlBooleanTruthCases.Prefix + item.Clause + SqlBooleanTruthCases.Tail, SqlBooleanTruthCases.Parameters(),
                AllowFullScan: true))).Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst(SqlNullOrderingProtocol.Reader,
            SqlBooleanTruthCases.Ast(database.Partition, item))).Code).IsEqualTo(ErrorCode.Validation);
        await SqlNullOrderingRefusals.UnchangedAsync(database.Store, before, position, identity);
        await SqlBooleanOperandControls.FaultAsync(database, engine, before, position, identity);
        var repairedCommand = new CommandRequest(Guid.NewGuid(), database.Partition,
            [new PutDocument(SqlNullOrderingProtocol.Collection, Low, HealthyJson, InitialRevision + InitialRevision)]);
        var repaired = database.Submit(OperationKind.Batch, repairedCommand, id: repairedCommand.CommandId).Get<CommitReceipt>();
        await Assert.That(repaired.CommandId).IsNotEqualTo(fault.CommandId);
        await Assert.That(repaired.Mutations).IsEquivalentTo([new MutationReceipt(PutKind,
            SqlNullOrderingProtocol.Collection, Low, RepairedRevision)], global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
        var healthy = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var cut = database.Store.Position;
        await SqlNullOrderingPages.ReplayAsync(database.Database, faultCommand, fault);
        await SqlNullOrderingPages.ReplayAsync(database.Database, repairedCommand, repaired);
        await SqlNullOrderingRefusals.UnchangedAsync(database.Store, healthy, cut, identity);
    }

    private const int First = 0;
    private const int AgainstOne = 0;
    internal const long InitialRevision = 1;
    internal const long RepairedRevision = 3;
    private const string Low = "low";
    private const string PutKind = "putDocument";
    private const string FaultJson = "{\"label\":\"low\",\"score\":1}";
    private const string HealthyJson = "{\"label\":\"low\",\"score\":true}";
}
