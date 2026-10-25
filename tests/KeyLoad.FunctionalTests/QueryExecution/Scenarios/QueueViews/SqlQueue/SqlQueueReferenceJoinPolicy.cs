using KeyLoad.Query;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class SqlQueueReferenceJoinPolicy
{
    internal static async Task RunAsync(TestDatabase database)
    {
        var principal = new PrincipalRecord(SqlQueueReferenceJoinFixture.Reader, database.Partition.TenantId,
            [new(database.Partition.DatabaseId, SqlQueueReferenceJoinFixture.Queue, Capability.Query | Capability.QueueInspect),
                new(database.Partition.DatabaseId, SqlQueueReferenceJoinFixture.EmptyQueue, Capability.Query | Capability.QueueInspect),
                new(database.Partition.DatabaseId, SqlQueueReferenceJoinFixture.Target, Capability.Query | Capability.DocumentsRead)],
            [SqlQueueReferenceJoinFixture.RefRead])
        {
            RestrictRows = true,
            OwnerId = SqlQueueReferenceJoinFixture.Reader
        };
        var persisted = database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>();
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var request = SqlQueueReferenceJoinFixture.Request(database.Partition);
        await SqlQueueReferenceJoinAssertions.RejectAsync(database, () => engine.Execute(principal.Id, request), ErrorCode.PermissionDenied);
        await SqlQueueReferenceJoinAssertions.RejectAsync(database,
            () => engine.Execute(principal.Id, SqlQueueReferenceJoinFixture.Request(database.Partition, SqlQueueReferenceJoinFixture.EmptyQueue)),
            ErrorCode.PermissionDenied);
        var repaired = database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(persisted with
        {
            PolicyEpoch = persisted.PolicyEpoch + 1,
            FieldGrants = [SqlQueueReferenceJoinFixture.RefRead, SqlQueueReferenceJoinFixture.RefUse]
        }))
            .Get<PrincipalRecord>();
        await SqlQueueReferenceJoinAssertions.PageAsync(engine.Execute(principal.Id, request), database.Store.Position, restricted: true, masked: true);
        var empty = engine.Execute(principal.Id, SqlQueueReferenceJoinFixture.Request(database.Partition, SqlQueueReferenceJoinFixture.EmptyQueue));
        await Assert.That(empty.Rows).IsEmpty();
        await Assert.That(empty.Cursor).IsNull();
        await Assert.That(empty.CutPosition).IsEqualTo(database.Store.Position);
        var revoked = database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(repaired with
        {
            PolicyEpoch = repaired.PolicyEpoch + 1,
            FieldGrants = [SqlQueueReferenceJoinFixture.RefRead]
        })).Get<PrincipalRecord>();
        await SqlQueueReferenceJoinAssertions.RejectAsync(database, () => engine.Execute(principal.Id, request), ErrorCode.PermissionDenied);
        database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(revoked with
        {
            PolicyEpoch = revoked.PolicyEpoch + 1,
            FieldGrants = [SqlQueueReferenceJoinFixture.RefRead, SqlQueueReferenceJoinFixture.RefUse, SqlQueueReferenceJoinFixture.NameRead]
        }));
        await SqlQueueReferenceJoinAssertions.PageAsync(engine.Execute(principal.Id, request), database.Store.Position, restricted: true);
        await VerifyMissingRepairAsync(database);
    }

    private static async Task VerifyMissingRepairAsync(TestDatabase database)
    {
        database.Commit(new PutDocument(SqlQueueReferenceJoinFixture.Target, "m", SqlQueueReferenceJoinFixture.RepairedJson,
            Access: new(OwnerId: SqlQueueReferenceJoinFixture.Reader)));
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        await SqlQueueReferenceJoinAssertions.PageAsync(engine.Execute(SqlQueueReferenceJoinFixture.Reader,
            SqlQueueReferenceJoinFixture.Request(database.Partition)), database.Store.Position, restricted: true, repaired: true);
        await SqlQueueReferenceJoinOperation.VerifyAsync(database, repaired: true);
    }
}
