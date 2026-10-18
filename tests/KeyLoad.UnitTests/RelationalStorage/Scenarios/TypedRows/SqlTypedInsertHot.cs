using KeyLoad.UnitTests.Features.Messaging;
using ManagedCode.Communication;
namespace KeyLoad.UnitTests.Features.RelationalStorage;

internal static class SqlTypedInsertHot
{
    internal static async Task<SqlTypedInsertState> RunAsync(TestDatabase fixture, CancellationToken token)
    {
        var definition = SqlTypedInsertLiteral.Definition(fixture.Partition);
        await SqlTypedInsertAssertions.SameAsync(definition, fixture.Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(fixture.Partition.TenantId, fixture.Partition.DatabaseId, definition)).Get<ResourceDefinition>());
        var principal = new PrincipalRecord(SqlTypedInsertLiteral.Writer, fixture.Partition.TenantId,
            [new(fixture.Partition.DatabaseId, SqlTypedInsertLiteral.Table, Capability.DocumentsRead | Capability.DocumentsWrite | Capability.Query)],
            [SqlTypedInsertLiteral.ReadGrant, SqlTypedInsertLiteral.UseGrant, SqlTypedInsertLiteral.WriteGrant]);
        await SqlTypedInsertAssertions.SameAsync(principal, fixture.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>());
        await Assert.That(principal.ClusterAdministrator).IsFalse();
        var created = Execute(fixture, SqlTypedInsertLiteral.Sql, token);
        await SqlTypedInsertAssertions.ReceiptAsync(created,
            new(PutKind, SqlTypedInsertLiteral.Table, SqlTypedInsertLiteral.One, InitialRevision),
            new(PutKind, SqlTypedInsertLiteral.Table, SqlTypedInsertLiteral.Two, InitialRevision));
        await SqlTypedInsertAssertions.ImageAsync(fixture, fixture.Store, fixture.Database, repaired: false);
        var failed = await RefuseAsync(fixture, SqlTypedInsertLiteral.Duplicate, ErrorCode.Conflict, token);
        await RefuseAsync(fixture, SqlTypedInsertLiteral.Rollback, ErrorCode.Conflict, token);
        await RefuseAsync(fixture, SqlTypedInsertLiteral.UniqueRollback, ErrorCode.Conflict, token);
        await Assert.That(fixture.Database.GetDocument(SqlTypedInsertLiteral.Root,
            new(fixture.Partition, SqlTypedInsertLiteral.Table, SqlTypedInsertLiteral.Four), cancellationToken: token)).IsNull();
        await Assert.That(fixture.Database.GetDocument(SqlTypedInsertLiteral.Root,
            new(fixture.Partition, SqlTypedInsertLiteral.Table, SqlTypedInsertLiteral.Five), cancellationToken: token)).IsNull();
        await RefuseAsync(fixture, TypeFault, ErrorCode.Validation, token);
        await RefuseAsync(fixture, DecimalFault, ErrorCode.Validation, token);
        await RefuseAsync(fixture, TimestampFault, ErrorCode.Validation, token);
        await RefuseAsync(fixture, MissingFault, ErrorCode.Validation, token);
        await RefuseAsync(fixture, PrimaryFault, ErrorCode.Validation, token);
        await ReplayAsync(fixture, created);
        return new(principal, created, failed);
    }
    internal static SqlTypedInsertExecution Execute(TestDatabase fixture, string sql, CancellationToken token)
    {
        return Execute(fixture.Database, fixture.Store, fixture.Partition, sql, token);
    }
    internal static SqlTypedInsertExecution Execute(global::KeyLoad.Core.DatabaseEngine database,
        global::KeyLoad.Storage.ZoneTree.ZoneTreeStore store, PartitionRef partition, string sql, CancellationToken token)
    {
        var command = SqlTypedInsertLiteral.Compile(partition, Guid.NewGuid(), sql, token);
        var at = database.EvaluationClock.GetUtcNow();
        var placement = store.Read(view => global::KeyLoad.Core.DatabaseEngine.ReadPlacementWitness(view, partition));
        var expectedToken = new CommitToken(placement.Incarnation, partition.AtomicPartitionId,
            checked(store.Position + NextCommit));
        var result = SqlTypedInsertLiteral.Apply(database, command, at);
        return new(command, at, result, SqlTypedInsertAssertions.OutcomeBytes(store, partition, command.CommandId), expectedToken);
    }
    internal static async Task<SqlTypedInsertExecution> RefuseAsync(TestDatabase fixture, string sql, ErrorCode expected, CancellationToken token)
    {
        var before = fixture.Database.GetOutboxStatus(SqlTypedInsertLiteral.Root, fixture.Partition);
        var execution = Execute(fixture, sql, token);
        var detail = sql switch
        {
            SqlTypedInsertLiteral.UniqueRollback => UniqueDetail,
            TypeFault or DecimalFault or TimestampFault => TypeDetail,
            MissingFault => MissingDetail,
            PrimaryFault => PrimaryDetail,
            _ => ExistingDetail
        };
        await SqlTypedInsertAssertions.SameAsync(Result<object>.Fail(Errors.Problem(expected, detail)), execution.Result);
        await SqlTypedInsertAssertions.SameAsync(before, fixture.Database.GetOutboxStatus(SqlTypedInsertLiteral.Root, fixture.Partition));
        await SqlTypedInsertAssertions.ImageAsync(fixture, fixture.Store, fixture.Database, repaired: false);
        await ReplayAsync(fixture, execution);
        return execution;
    }
    internal static async Task ReplayAsync(TestDatabase fixture, SqlTypedInsertExecution execution)
    {
        var image = QueueWholeFlowStorage.Bytes(fixture.Store);
        var cut = fixture.Store.Position;
        await SqlTypedInsertAssertions.SameAsync(execution.Result,
            SqlTypedInsertLiteral.Apply(fixture.Database, execution.Command, execution.At));
        await SqlTypedInsertAssertions.UnchangedAsync(fixture.Store, image, cut);
        await SqlTypedInsertAssertions.SameAsync(execution.Bytes,
            SqlTypedInsertAssertions.OutcomeBytes(fixture.Store, fixture.Partition, execution.Command.CommandId));
    }
    private const string DecimalFault = "INSERT INTO [sql-values] (id,title,price) VALUES (N'four',N'delta',N'wrong')";
    private const string TimestampFault = "INSERT INTO [sql-values] (id,title,at) VALUES (N'four',N'delta',N'2026-10-14T01:00:00+01:00')";
    private const long NextCommit = 1;
    private const string ExistingDetail = "The inserted canonical row already exists.";
    private const string UniqueDetail = "A partition-scoped unique index value is already present.";
    private const string TypeDetail = "A relational column value does not match its type or nullability.";
    private const string MissingDetail = "A required relational column is missing.";
    private const string PrimaryDetail = "The relational primary key must match the canonical entity identifier.";
    private const string TypeFault = "INSERT INTO [sql-values] (id,title,amount) VALUES (N'four',N'delta',N'wrong')";
    private const string MissingFault = "INSERT INTO [sql-values] (id,amount) VALUES (N'four',4)";
    private const string PrimaryFault = "INSERT INTO [sql-values] (title,amount) VALUES (N'delta',4)";
    private const string PutKind = "putDocument";
    private const long InitialRevision = 1;
}
