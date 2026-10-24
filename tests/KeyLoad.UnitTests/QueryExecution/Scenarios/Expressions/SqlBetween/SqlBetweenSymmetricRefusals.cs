using KeyLoad.Query;

namespace KeyLoad.UnitTests.QueryExecution;

internal static class SqlBetweenSymmetricRefusals
{
    internal static async Task InvalidAsync(TestDatabase database)
    {
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var before = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var position = database.Store.Position;
        var identity = JsonDefaults.Serialize(database.Store.Identity);
        foreach (var clause in new[] { "score BETWEEN 2", "score BETWEEN 2 OR 1", "score BETWEEN SYMMETRIC 2 1", "score BETWEEN SYMMETRIC 2 OR 1",
            "score NOT BETWEEN SYMMETRIC 2 1",
            "score BETWEEN ASYMMETRIC 2 1", "score NOT BETWEEN ASYMMETRIC 2 OR 1",
            "score BETWEEN ASYMMETRIC ASYMMETRIC 1 AND 2",
            "score BETWEEN SYMMETRIC ASYMMETRIC 1 AND 2" })
        {
            var actual = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(SqlNullOrderingProtocol.Reader,
                new(database.Partition, SqlBetweenSymmetricCases.Prefix + clause + SqlBetweenSymmetricCases.Tail, AllowFullScan: true)));
            await Assert.That(actual.Code).IsEqualTo(ErrorCode.Validation);
            await SqlNullOrderingRefusals.UnchangedAsync(database.Store, before, position, identity);
            await SqlBetweenSymmetricPages.HealthyAsync(database.Database, database.Store, database.Partition, InitialRevision);
        }
        foreach (var clause in new[] { "score BETWEEN SYMMETRIC (2) AND 1", "score BETWEEN ABS(2) AND 1" })
        {
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(SqlNullOrderingProtocol.Reader,
                new(database.Partition, SqlBetweenSymmetricCases.Prefix + clause + SqlBetweenSymmetricCases.Tail,
                    AllowFullScan: true))).Code).IsEqualTo(ErrorCode.UnsupportedCapability);
            await SqlNullOrderingRefusals.UnchangedAsync(database.Store, before, position, identity);
            await SqlBetweenSymmetricPages.HealthyAsync(database.Database, database.Store, database.Partition, InitialRevision);
            await SqlNullOrderingRefusals.UnchangedAsync(database.Store, before, position, identity);
        }
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        foreach (var clause in new[] { SqlBetweenSymmetricCases.All[First].Clause, SqlBetweenSymmetricCases.AsymmetricHealthy })
        {
            var request = new QueryRequest(database.Partition, SqlBetweenSymmetricCases.Prefix
                + clause + SqlBetweenSymmetricCases.Tail, AllowFullScan: true);
            var error = Assert.ThrowsExactly<OperationCanceledException>(() => engine.Execute(SqlNullOrderingProtocol.Reader,
                request, cancellationToken: cancelled.Token));
            await Assert.That(error.CancellationToken).IsEqualTo(cancelled.Token);
            await SqlNullOrderingRefusals.UnchangedAsync(database.Store, before, position, identity);
            using var healthy = new CancellationTokenSource();
            await Assert.That(healthy.Token.IsCancellationRequested).IsFalse();
            var expected = new global::KeyLoad.QueryPage([
                new("high", InitialRevision, "{\"label\":\"high\"}", RedactedFields: []),
                new(Low, InitialRevision, "{\"label\":\"low\"}", RedactedFields: [])],
                null, position, "bounded-full-scan");
            await SqlNullOrderingPages.EqualAsync(expected, engine.Execute(SqlNullOrderingProtocol.Reader,
                request, cancellationToken: healthy.Token));
            await SqlBetweenSymmetricPages.HealthyAsync(database.Database, database.Store, database.Partition, InitialRevision);
            await SqlNullOrderingRefusals.UnchangedAsync(database.Store, before, position, identity);
        }
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
        foreach (var item in new[] { SqlBetweenSymmetricCases.All[AgainstOne],
            SqlBetweenSymmetricCases.All.Single(value => value.Clause == SqlBetweenSymmetricCases.AsymmetricFault) })
        {
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(SqlNullOrderingProtocol.Reader,
                new(database.Partition, SqlBetweenSymmetricCases.Prefix + item.Clause + SqlBetweenSymmetricCases.Tail,
                    AllowFullScan: true))).Code).IsEqualTo(ErrorCode.Validation);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst(SqlNullOrderingProtocol.Reader,
                SqlBetweenSymmetricCases.Ast(database.Partition, item))).Code).IsEqualTo(ErrorCode.Validation);
            await SqlNullOrderingRefusals.UnchangedAsync(database.Store, before, position, identity);
        }
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
