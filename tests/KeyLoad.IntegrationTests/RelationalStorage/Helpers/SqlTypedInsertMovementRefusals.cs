using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using Npgsql;
namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal static class SqlTypedInsertMovementRefusals
{
    internal static async Task RejectAsync(RequestCqrsRf3Callers caller, PostgresRf3Caller wire,
        SqlOperationRequest request, ErrorCode error, string state, CancellationToken token)
    {
        await SqlCreateTableRf3Failures.SdkAsync(caller.Sdk, request, error, token);
        await SqlCreateTableRf3Failures.McpAsync(caller.Mcp, request, error, dispatched: true, token);
        await SqlTypedInsertMovementClients.ArmAsync(wire, request, token);
        var refused = await Assert.ThrowsAsync<PostgresException>(() => wire.ExecuteAsync<CommitReceipt>(request.Sql, [], prepared: true, token))
            ?? throw new InvalidOperationException(SqlTypedInsertMovementProtocol.Missing);
        await Assert.That(refused.SqlState).IsEqualTo(state);
    }
    internal static Task RunAsync(TwoRf3MembershipWave wave, SqlTypedInsertMovementTls tls,
        PartitionMovementPublicParentRf3Seed seed, SqlTypedInsertMovementState state, CancellationToken token)
        => SqlTypedInsertMovementClients.RunAsync(wave, tls, state, TwoRf3MembershipProtocol.Node4, async (caller, wire) =>
    {
        var one = SqlTypedInsertMovementProtocol.Request(seed.Partition, SqlTypedInsertMovementProtocol.StaleId,
                SqlTypedInsertMovementProtocol.AfterSql, SqlTypedInsertMovementProtocol.InitialEpoch);
        await RejectAsync(caller, wire, one, ErrorCode.OwnershipLost, SqlTypedInsertMovementProtocol.OwnershipState, token);
        var future = one with
        {
            CommandId = SqlTypedInsertMovementProtocol.FutureId,
            OwnershipEpoch = SqlTypedInsertMovementProtocol.FutureEpoch
        };
        await RejectAsync(caller, wire, future, ErrorCode.OwnershipLost, SqlTypedInsertMovementProtocol.OwnershipState, token);
        await SqlCreateTableRf3Failures.WireAsync(wire, one.Sql, SqlTypedInsertMovementProtocol.ValidationState, token);
        var reference = new EntityRef(seed.Partition, SqlTypedInsertMovementProtocol.Table, SqlTypedInsertMovementProtocol.One);
        var old = await caller.Sdk.GetAsync(reference, state.Receipt.Token, token);
        await Assert.That(old.IsSuccess).IsFalse();
        await Assert.That(old.Value).IsNull();
        await Assert.That(old.Problem!.ErrorCode).IsEqualTo(nameof(ErrorCode.TokenInvalidated));
        await McpCallerAssertions.ErrorAsync(await caller.Mcp.CallAsync(McpCallerTools.DocumentsGet,
            new GetDocumentRequest(reference, state.Receipt.Token), token), ErrorCode.TokenInvalidated, dispatched: true);
        await SqlTypedInsertMovementPages.RowsAsync(seed.Target, caller, wire, seed.Partition, false, state.Receipt, token);
    }, token);
}

