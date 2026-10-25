using KeyLoad.Query;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class SqlEventReferenceJoinPolicy
{
    internal static async Task<(CommandRequest Command, CommitReceipt Receipt)> RunAsync(TestDatabase database)
    {
        var principal = new PrincipalRecord(SqlEventReferenceJoinFixture.Reader, database.Partition.TenantId,
            [new(database.Partition.DatabaseId, SqlEventReferenceJoinFixture.StreamSet, Capability.Query | Capability.EventsRead),
                new(database.Partition.DatabaseId, SqlEventReferenceJoinFixture.EmptyStreamSet, Capability.Query | Capability.EventsRead),
                new(database.Partition.DatabaseId, SqlEventReferenceJoinFixture.Target, Capability.Query | Capability.DocumentsRead)],
            [SqlEventReferenceJoinFixture.RefRead])
        {
            RestrictRows = true,
            OwnerId = SqlEventReferenceJoinFixture.Reader
        };
        var persisted = database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>();
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var request = SqlEventReferenceJoinFixture.Request(database.Partition);
        await SqlEventReferenceJoinAssertions.RejectAsync(database, () => engine.Execute(principal.Id, request), ErrorCode.PermissionDenied);
        await SqlEventReferenceJoinAssertions.RejectAsync(database,
            () => engine.Execute(principal.Id, SqlEventReferenceJoinFixture.Request(database.Partition, SqlEventReferenceJoinFixture.EmptyStreamSet)),
            ErrorCode.PermissionDenied);
        var repaired = database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(persisted with
        {
            PolicyEpoch = persisted.PolicyEpoch + 1,
            FieldGrants = [SqlEventReferenceJoinFixture.RefRead, SqlEventReferenceJoinFixture.RefUse]
        }))
            .Get<PrincipalRecord>();
        await SqlEventReferenceJoinAssertions.PageAsync(engine.Execute(principal.Id, request), database.Store.Position, restricted: true, masked: true);
        var empty = engine.Execute(principal.Id, SqlEventReferenceJoinFixture.Request(database.Partition, SqlEventReferenceJoinFixture.EmptyStreamSet));
        await Assert.That(empty.Rows).IsEmpty();
        await Assert.That(empty.Cursor).IsNull();
        await Assert.That(empty.CutPosition).IsEqualTo(database.Store.Position);
        var revoked = database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(repaired with
        {
            PolicyEpoch = repaired.PolicyEpoch + 1,
            FieldGrants = [SqlEventReferenceJoinFixture.RefRead]
        })).Get<PrincipalRecord>();
        await SqlEventReferenceJoinAssertions.RejectAsync(database, () => engine.Execute(principal.Id, request), ErrorCode.PermissionDenied);
        database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(revoked with
        {
            PolicyEpoch = revoked.PolicyEpoch + 1,
            FieldGrants = [SqlEventReferenceJoinFixture.RefRead, SqlEventReferenceJoinFixture.RefUse, SqlEventReferenceJoinFixture.NameRead]
        }));
        await SqlEventReferenceJoinAssertions.PageAsync(engine.Execute(principal.Id, request), database.Store.Position, restricted: true);
        return await VerifyMissingRepairAsync(database);
    }

    private static async Task<(CommandRequest Command, CommitReceipt Receipt)> VerifyMissingRepairAsync(TestDatabase database)
    {
        var command = new CommandRequest(Guid.NewGuid(), database.Partition,
            [new PutDocument(SqlEventReferenceJoinFixture.Target, "m", SqlEventReferenceJoinFixture.RepairedJson,
                Access: new(OwnerId: SqlEventReferenceJoinFixture.Reader))]);
        var receipt = database.Submit(OperationKind.Batch, command, id: command.CommandId).Get<CommitReceipt>();
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        await SqlEventReferenceJoinAssertions.PageAsync(engine.Execute(SqlEventReferenceJoinFixture.Reader,
            SqlEventReferenceJoinFixture.Request(database.Partition)), database.Store.Position, restricted: true, repaired: true);
        await SqlEventReferenceJoinOperation.VerifyAsync(database, repaired: true);
        return (command, receipt);
    }
}
