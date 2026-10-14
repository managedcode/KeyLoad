using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlTypedInsertRf3Wave
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
                    await SqlRf3Protocol.EqualAsync(state.Owner, await McpCallerAssertions.SdkSuccessAsync(
                        await root.ReadAtomicPartitionPlacementAsync(new(SqlTypedInsertRf3Hot.PlacementVersion,
                            SqlTypedInsertRf3Literal.Partition), token)));
                    var before = await SqlTypedInsertRf3Images.ReadAsync(root, token);
                    if (state.EpochFenced)
                    {
                        await FenceAsync(caller, wire, state.Created, token);
                        await FenceAsync(caller, wire, state.Failed, token);
                    }
                    else
                    {
                        await ReplayAsync(caller, wire, state.Created, state.Receipt, token);
                        await SqlCreateTableRf3Failures.SdkAsync(caller.Sdk, state.Failed, ErrorCode.Conflict, token);
                        await SqlCreateTableRf3Failures.McpAsync(caller.Mcp, state.Failed, ErrorCode.Conflict, dispatched: true, token);
                        await SqlTypedInsertRf3Wire.RefuseAsync(wire, state.Failed, SqlCreateTableRf3Literal.ConflictState, token);
                    }
                    if (state.Cancelled is { } cancelled && state.CancelledReceipt is { } cancelledReceipt)
                    {
                        if (state.EpochFenced)
                        { await FenceAsync(caller, wire, cancelled, token); }
                        else
                        { await ReplayAsync(caller, wire, cancelled, cancelledReceipt, token); }
                    }
                    if (state.Repaired is { } repaired && state.RepairedReceipt is { } receipt)
                    { await ReplayAsync(caller, wire, repaired, receipt, token); }
                    foreach (var cold in state.ColdReceipts ?? [])
                    { await ReplayAsync(caller, wire, cold.Request, cold.Receipt, token); }
                    await SqlTypedInsertRf3Pages.RunAsync(caller, wire, state, token);
                    await SqlTypedInsertRf3Images.RequireAsync(root, before, token);
                }, failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            return true;
        }, token);
    internal static async Task ReplayAsync(RequestCqrsRf3Callers caller, PostgresRf3Caller wire,
        SqlOperationRequest request, CommitReceipt expected, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.SdkAsync<CommitReceipt>(caller.Sdk, request, token));
        await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.McpAsync<CommitReceipt>(caller.Mcp, request, token));
        await SqlRf3Protocol.EqualAsync(expected, await SqlTypedInsertRf3Wire.ExecuteAsync(wire, request, token));
    }
    internal static async Task FenceAsync(RequestCqrsRf3Callers caller, PostgresRf3Caller wire,
        SqlOperationRequest request, CancellationToken token)
    {
        await SqlCreateTableRf3Failures.SdkAsync(caller.Sdk, request, ErrorCode.PermissionDenied, token);
        await SqlCreateTableRf3Failures.McpAsync(caller.Mcp, request, ErrorCode.PermissionDenied, dispatched: true, token);
        await SqlTypedInsertRf3Wire.RefuseAsync(wire, request, SqlCreateTableRf3Literal.PermissionState, token);
    }
}
