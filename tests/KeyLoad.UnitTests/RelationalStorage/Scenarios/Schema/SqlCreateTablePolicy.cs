using KeyLoad.UnitTests.Messaging;
using ManagedCode.Communication;
namespace KeyLoad.UnitTests.RelationalStorage;

internal static class SqlCreateTablePolicy
{
    private const string Administrator = "sql-create-administrator";
    private const long DeniedEpoch = 2;
    private const long RepairedEpoch = 3;
    private const string RepairTable = "sql-repaired";
    private const string RepairSql = "CREATE TABLE [sql-repaired] (id NVARCHAR(MAX) NOT NULL PRIMARY KEY, title NVARCHAR(MAX) NOT NULL, amount BIGINT NOT NULL, active BIT NOT NULL)";
    private const string EpochChanged = "The principal policy changed since this command was evaluated.";
    internal static async Task<PrincipalRecord> SeedAsync(TestDatabase fixture)
    {
        var principal = new PrincipalRecord(Administrator, fixture.Partition.TenantId,
            [new(fixture.Partition.DatabaseId, "*", Capability.All)], ["*"])
        {
            ClusterAdministrator = true
        };
        await ConfigureAsync(fixture, principal);
        return principal;
    }
    private static async Task ConfigureAsync(TestDatabase fixture, PrincipalRecord principal)
    {
        var actual = fixture.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>();
        await SqlCreateTableAssertions.SameAsync(principal, actual);
    }
    internal static async Task<SqlCreateTableState> RunAsync(TestDatabase fixture, SqlCreateTableState state, CancellationToken token)
    {
        await ConfigureAsync(fixture, state.Principal with
        {
            ClusterAdministrator = false,
            PolicyEpoch = DeniedEpoch
        });
        var denied = SqlCreateTableLiteral.Compile(fixture.Partition, Guid.NewGuid(), RepairSql, token);
        var issued = fixture.Database.EvaluationClock.GetUtcNow();
        var before = fixture.Database.GetOutboxStatus(SqlCreateTableLiteral.Root, fixture.Partition);
        await Assert.That(Errors.Code(SqlCreateTableLiteral.Apply(fixture.Database, denied, issued, state.Principal.Id).Problem))
            .IsEqualTo(ErrorCode.PermissionDenied);
        await SqlCreateTableAssertions.SameAsync(before, fixture.Database.GetOutboxStatus(SqlCreateTableLiteral.Root, fixture.Partition));
        var image = QueueWholeFlowStorage.Bytes(fixture.Store);
        var cut = fixture.Store.Position;
        await Assert.That(Errors.Code(SqlCreateTableLiteral.Apply(fixture.Database, state.Created, state.CreatedAt, state.Principal.Id).Problem))
            .IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(Errors.Code(SqlCreateTableLiteral.Apply(fixture.Database, state.Duplicate, state.DuplicateAt, state.Principal.Id).Problem))
            .IsEqualTo(ErrorCode.PermissionDenied);
        await SqlCreateTableAssertions.UnchangedAsync(fixture.Store, image, cut);
        await SqlCreateTableOutcomeBytes.RequireAsync(fixture.Store, state);
        await ConfigureAsync(fixture, state.Principal with
        {
            PolicyEpoch = RepairedEpoch
        });
        state = state with
        {
            EpochChanged = true
        };
        image = QueueWholeFlowStorage.Bytes(fixture.Store);
        cut = fixture.Store.Position;
        await ReplayAsync(fixture.Database, state);
        await SqlCreateTableAssertions.SameAsync(Result<object>.Fail(Errors.Problem(ErrorCode.PermissionDenied, EpochChanged)),
            SqlCreateTableLiteral.Apply(fixture.Database, denied, issued, state.Principal.Id));
        await SqlCreateTableAssertions.UnchangedAsync(fixture.Store, image, cut);
        var healthy = SqlCreateTableLiteral.Compile(fixture.Partition, Guid.NewGuid(), RepairSql, token);
        var healthyAt = fixture.Database.EvaluationClock.GetUtcNow();
        var result = SqlCreateTableLiteral.Apply(fixture.Database, healthy, healthyAt, state.Principal.Id);
        await Assert.That(Errors.Code(result.Problem)).IsNull();
        await SqlCreateTableAssertions.SameAsync(SqlCreateTableLiteral.Definition(fixture.Partition) with
        {
            Name = RepairTable
        },
            result.Get<ResourceDefinition>());
        await SqlCreateTableAssertions.RowsAsync(fixture, fixture.Store, fixture.Database);
        await SqlCreateTableOutcomeBytes.RequireAsync(fixture.Store, state);
        state = state with
        {
            Repaired = healthy,
            RepairedAt = healthyAt,
            RepairedOutcome = result
        };
        image = QueueWholeFlowStorage.Bytes(fixture.Store);
        cut = fixture.Store.Position;
        await ReplayAsync(fixture.Database, state);
        await SqlCreateTableAssertions.UnchangedAsync(fixture.Store, image, cut);
        return state;
    }
    internal static async Task ReplayAsync(DatabaseEngine database, SqlCreateTableState state)
    {
        var expected = Result<object>.Fail(Errors.Problem(ErrorCode.PermissionDenied, EpochChanged));
        await SqlCreateTableAssertions.SameAsync(state.EpochChanged ? expected : state.Original,
            SqlCreateTableLiteral.Apply(database, state.Created, state.CreatedAt, state.Principal.Id));
        await SqlCreateTableAssertions.SameAsync(state.EpochChanged ? expected : state.Failed,
            SqlCreateTableLiteral.Apply(database, state.Duplicate, state.DuplicateAt, state.Principal.Id));
        if (state.Repaired is { } repaired)
        {
            await SqlCreateTableAssertions.SameAsync(state.RepairedOutcome,
            SqlCreateTableLiteral.Apply(database, repaired, state.RepairedAt, state.Principal.Id));
        }
    }
}
