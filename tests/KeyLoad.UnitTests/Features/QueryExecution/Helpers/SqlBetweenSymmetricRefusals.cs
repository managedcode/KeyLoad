using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlBetweenSymmetricRefusals
{
    internal static async Task InvalidAsync(TestDatabase database)
    {
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var before = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var position = database.Store.Position;
        var identity = JsonDefaults.Serialize(database.Store.Identity);
        foreach (var clause in new[] { "score BETWEEN SYMMETRIC 2 1", "score BETWEEN SYMMETRIC 2 OR 1",
            "score BETWEEN SYMMETRIC (2) AND 1", "score NOT BETWEEN SYMMETRIC 2 1" })
        {
            var actual = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(SqlNullOrderingProtocol.Reader,
                new(database.Partition, SqlBetweenSymmetricCases.Prefix + clause + SqlBetweenSymmetricCases.Tail, AllowFullScan: true)));
            await Assert.That(actual.Code).IsEqualTo(ErrorCode.Validation);
            await SqlNullOrderingRefusals.UnchangedAsync(database.Store, before, position, identity);
        }
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var request = new QueryRequest(database.Partition, SqlBetweenSymmetricCases.Prefix
            + SqlBetweenSymmetricCases.All[First].Clause + SqlBetweenSymmetricCases.Tail, AllowFullScan: true);
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
        var item = SqlBetweenSymmetricCases.All[AgainstOne];
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(SqlNullOrderingProtocol.Reader,
            new(database.Partition, SqlBetweenSymmetricCases.Prefix + item.Clause + SqlBetweenSymmetricCases.Tail,
                AllowFullScan: true))).Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst(SqlNullOrderingProtocol.Reader,
            SqlBetweenSymmetricCases.Ast(database.Partition, item))).Code).IsEqualTo(ErrorCode.Validation);
        await SqlNullOrderingRefusals.UnchangedAsync(database.Store, before, position, identity);
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
    private const string FaultJson = "{\"label\":\"low\",\"score\":true}";
    private const string HealthyJson = "{\"label\":\"low\",\"score\":1}";
}
