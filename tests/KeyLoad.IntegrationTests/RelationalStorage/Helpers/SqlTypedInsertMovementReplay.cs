using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal static class SqlTypedInsertMovementReplay
{
    internal static Task RunAsync(TwoRf3MembershipWave wave, SqlTypedInsertMovementTls tls,
        SqlTypedInsertMovementState state, CancellationToken token)
        => SqlTypedInsertMovementClients.RunAsync(wave, tls, state, TwoRf3MembershipProtocol.Node1, async (caller, wire) =>
        {
            if (state.PolicyFenced)
            {
                foreach (var request in new[] { state.Original, state.Failed })
                {
                    await SqlTypedInsertMovementRefusals.RejectAsync(caller, wire, request, ErrorCode.PermissionDenied, SqlTypedInsertMovementProtocol.PermissionState, token);
                }
            }
            else
            {
                await SqlRf3Protocol.EqualAsync(state.Receipt, await SqlRf3Protocol.SdkAsync<CommitReceipt>(caller.Sdk, state.Original, token));
                await SqlRf3Protocol.EqualAsync(state.Receipt, await SqlRf3Protocol.McpAsync<CommitReceipt>(caller.Mcp, state.Original, token));
                await SqlTypedInsertMovementClients.ArmAsync(wire, state.Original, token);
                await SqlRf3Protocol.EqualAsync(state.Receipt, await wire.ExecuteAsync<CommitReceipt>(state.Original.Sql, [], prepared: true, token));
                await SqlTypedInsertMovementRefusals.RejectAsync(caller, wire, state.Failed, ErrorCode.OwnershipLost,
                    SqlTypedInsertMovementProtocol.OwnershipState, token);
            }
        }, token);
}

