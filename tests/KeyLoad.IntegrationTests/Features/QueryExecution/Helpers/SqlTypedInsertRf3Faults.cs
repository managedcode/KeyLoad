using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlTypedInsertRf3Faults
{
    internal static async Task RunAsync(ClusterFixture fixture, SqlTypedInsertRf3State state, KeyLoadClient root, CancellationToken token)
        => await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Identity.Secret, async caller =>
        {
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var wire = await PostgresRf3Caller.ConnectAsync(fixture.App, McpCallerProtocol.Node3,
                    state.Identity.Principal.Id, state.Identity.Secret, PostgresRf3Certificate.RootPath(fixture), token);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    var before = await SqlTypedInsertRf3Images.ReadAsync(root, token);
                    foreach (var (sql, error, sqlState) in Faults)
                    {
                        var request = SqlTypedInsertRf3Literal.Request(Guid.NewGuid(), sql);
                        await SqlCreateTableRf3Failures.SdkAsync(caller.Sdk, request, error, token);
                        await SqlCreateTableRf3Failures.McpAsync(caller.Mcp, request, error, dispatched: true, token);
                        await SqlTypedInsertRf3Wire.RefuseAsync(wire, request, sqlState, token);
                        await SqlTypedInsertRf3Images.RequireAsync(root, before, token);
                        await SqlTypedInsertRf3Pages.RunAsync(caller, wire, state, token);
                    }
                    var missing = new SqlOperationRequest(SqlTypedInsertRf3Literal.Partition, SqlTypedInsertRf3Literal.Repair);
                    await SqlCreateTableRf3Failures.SdkAsync(caller.Sdk, missing, ErrorCode.Validation, token);
                    await SqlCreateTableRf3Failures.McpAsync(caller.Mcp, missing, ErrorCode.Validation, dispatched: false, token);
                    await SqlCreateTableRf3Failures.WireAsync(wire, SqlTypedInsertRf3Literal.Repair, SqlCreateTableRf3Literal.ValidationState, token);
                    await SqlTypedInsertRf3Images.RequireAsync(root, before, token);
                    await SqlTypedInsertRf3Pages.RunAsync(caller, wire, state, token);
                }, failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            return true;
        }, token);
    private const string DecimalFault = "INSERT INTO [sql-values-rf3] (id,title,price) VALUES (N'four',N'delta',N'wrong')";
    private const string TimestampFault = "INSERT INTO [sql-values-rf3] (id,title,at) VALUES (N'four',N'delta',N'2026-10-14T01:00:00+01:00')";
    private static readonly (string Sql, ErrorCode Error, string State)[] Faults =
    [(SqlTypedInsertRf3Literal.Rollback, ErrorCode.Conflict, SqlCreateTableRf3Literal.ConflictState),
        (SqlTypedInsertRf3Literal.UniqueRollback, ErrorCode.Conflict, SqlCreateTableRf3Literal.ConflictState),
        (DecimalFault, ErrorCode.Validation, SqlCreateTableRf3Literal.ValidationState),
        (TimestampFault, ErrorCode.Validation, SqlCreateTableRf3Literal.ValidationState),
        (SqlTypedInsertRf3Literal.TypeFault, ErrorCode.Validation, SqlCreateTableRf3Literal.ValidationState),
        (SqlTypedInsertRf3Literal.MissingFault, ErrorCode.Validation, SqlCreateTableRf3Literal.ValidationState)];
}
